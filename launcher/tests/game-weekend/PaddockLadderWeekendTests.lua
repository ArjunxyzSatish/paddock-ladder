-- Runs inside the real game during run-weekend-test.ps1. It damages the car in practice, qualifying and the race,
-- and records what the game does around each session change. The script clicks the screens and closes the game.

local DATA_DIR = ac.getFolder(ac.FolderID.ACDocuments) .. '/paddock_ladder'
local REPORT_PATH = DATA_DIR .. '/weekend_test.json'
local APP_WINDOW = 'IMGUI_LUA_Paddock Ladder_main'

local report = { timeline = {}, stage = 'practice', screen = '' }

-- The fault rules, run in the game's own Lua.
do
  local Fault = dofile(ac.getFolder(ac.FolderID.Root) .. '/apps/lua/PaddockLadder/src/fault.lua')
  local function car(x, z, vx, vz)
    return { x = x, z = z, vx = vx, vz = vz, lookX = 0, lookZ = 1, sideX = 1, sideZ = 0 }
  end
  local cases = {
    { 'I rear-end a slower car', car(0, 0, 0, 30), car(0, 4.5, 0, 22), 0, 'you', 'rear' },
    { 'a faster car rear-ends me', car(0, 0, 0, 22), car(0, -4.5, 0, 30), 0, 'them', 'rear' },
    { 'level speeds nose to tail', car(0, 0, 0, 25), car(0, 4.5, 0, 25), 0, 'shared', 'rear' },
    { 'I am hit while spun', car(0, 0, 18, 2), car(0, -4.5, 0, 30), 0, 'shared', 'rear' },
    { 'I am hit while stopped', car(0, 0, 0, 0.5), car(0, -4.5, 0, 30), 0, 'shared', 'rear' },
    { 'I move across a car beside me', car(0, 0, 2, 30), car(2, 1, 0, 30), 0, 'you', 'side' },
    { 'a car moves across me', car(0, 0, 0, 30), car(2, 1, -2, 30), 0, 'them', 'side' },
    { 'both move together', car(0, 0, 1, 30), car(2, 1, -1, 30), 0, 'shared', 'side' },
    { 'I rejoin into a car', car(0, 0, 0, 30), car(2, 1, -2, 30), 4, 'you', 'side' },
  }
  report.faultFailures = {}
  for _, case in ipairs(cases) do
    local who, kind = Fault.rule(case[2], case[3], case[4])
    if who ~= case[5] or kind ~= case[6] then report.faultFailures[#report.faultFailures + 1] = case[1] .. ': got ' .. tostring(who) .. '/' .. tostring(kind) end
  end
  report.faultCases = #cases
end
local clock, stageClock, menuClock, saveClock, expiredClock = 0, 0, 0, 0, 0
local lastLine = ''

local function enter(stage)
  report.stage = stage
  stageClock = 0
end

local function note(sim)
  local session = ac.getSession(sim.currentSessionIndex)
  local line = string.format('session %d type %s menu %s paused %s started %s finished %s screen %s', sim.currentSessionIndex, tostring(session and session.type), tostring(sim.isInMainMenu), tostring(sim.isPaused), tostring(sim.isSessionStarted), tostring(sim.isSessionFinished), report.screen)
  if line ~= lastLine then
    lastLine = line
    report.timeline[#report.timeline + 1] = string.format('%.1f %s', clock, line)
  end
end

local function reportSkipButton()
  local window = ac.accessAppWindow(APP_WINDOW)
  if not window or not window:valid() then return end
  local position, size = window:position(), window:size()
  local s = math.max(0.8, math.min(1.8, size.x / 460))
  report.skipX, report.skipY = position.x + size.x - 54 * s, position.y + 73 * s
  report.windowVisible = window:visible()
end

function script.update(dt)
  local sim, car = ac.getSim(), ac.getCar(0)
  if not car then return end
  clock = clock + dt
  stageClock = stageClock + dt
  report.screen = ac.load('paddockladder.screen') or ''
  report.session = sim.currentSessionIndex
  report.paused = sim.isPaused
  report.body = { car.damage[0], car.damage[1], car.damage[2], car.damage[3] }
  report.engine = car.engineLifeLeft
  report.speed = car.speedKmh
  report.timeLeft = sim.sessionTimeLeft
  note(sim)

  -- The script clicks the garage screen. Whenever the game's own menu is up after that, this presses Drive.
  if sim.isInMainMenu and report.screen == '' then
    menuClock = menuClock + dt
    if menuClock > 2 then
      menuClock = 0
      ac.tryToStart(true)
    end
  else
    menuClock = 0
  end

  local onTrack = not sim.isInMainMenu and report.screen == ''
  if report.stage == 'practice' and onTrack and sim.currentSessionIndex == 0 then
    if stageClock > 3 then
      physics.setCarBodyDamage(0, vec4(40, 0, 0, 0))
      physics.setCarEngineLife(0, 850)
      enter('practice-damaged')
    end
  elseif report.stage == 'practice-damaged' then
    if stageClock > 4 then
      -- What the game's own pit menu does for free. The app has to put it back.
      physics.setCarBodyDamage(0, vec4(0, 0, 0, 0))
      enter('practice-wiped')
    end
  elseif report.stage == 'practice-wiped' then
    if stageClock > 3 then
      report.bodyAfterWipe = car.damage[0]
      enter('practice-wait')
    end
  elseif report.stage == 'practice-wait' then
    -- Let the clock run out by itself and see what the game does. If it just carries on, move things along.
    if sim.sessionTimeLeft <= 0 then expiredClock = expiredClock + dt end
    if expiredClock > 45 and not report.forcedSkip and sim.currentSessionIndex == 0 then
      report.forcedSkip = true
      ac.tryToSkipSession()
    end
    if sim.currentSessionIndex == 1 and onTrack then
      enter('qualifying')
    end
  elseif report.stage == 'qualifying' then
    if stageClock > 3 then
      report.qualifyingBody = car.damage[0]
      report.qualifyingEngine = car.engineLifeLeft
      physics.setCarBodyDamage(0, vec4(report.qualifyingBody, 30, 0, 0))
      enter('qualifying-damaged')
    end
  elseif report.stage == 'qualifying-damaged' then
    reportSkipButton()
    if sim.currentSessionIndex == 2 and onTrack then
      physics.setCarAutopilot(true, false)
      enter('race')
    end
  elseif report.stage == 'race' then
    if stageClock > 3 and not report.raceBody then
      report.raceBody = { car.damage[0], car.damage[1], car.damage[2], car.damage[3] }
      report.raceEngine = car.engineLifeLeft
    end
    if sim.isSessionStarted and stageClock > 10 then
      physics.setCarBodyDamage(0, vec4(car.damage[0] + 25, car.damage[1], car.damage[2], car.damage[3]))
      physics.setCarEngineLife(0, 700)
      enter('race-damaged')
    end
  elseif report.stage == 'race-damaged' then
    report.raceSpeed = math.max(report.raceSpeed or 0, car.speedKmh)
    local window = ac.accessAppWindow(APP_WINDOW)
    if window and window:valid() then
      local position, size = window:position(), window:size()
      report.winX, report.winY, report.winW, report.winScale = position.x, position.y, size.x, math.max(0.8, math.min(1.8, size.x / 460))
    end
    if stageClock > 22 then
      physics.setCarAutopilot(false, false)
      enter('pausing')
    end
  elseif report.stage == 'pausing' and stageClock > 1 then
    ac.tryToPause(true)
    enter('ready-to-quit')
  end

  saveClock = saveClock - dt
  if saveClock <= 0 then
    saveClock = 0.2
    io.save(REPORT_PATH, JSON.stringify(report), true)
  end
end

function script.windowMain(dt)
  ui.text('Paddock Ladder weekend test: ' .. report.stage)
end
