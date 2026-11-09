-- Paddock Ladder: the in-game half. One window shows position, contract, track limits and timing,
-- and reports how you drove to the launcher.

local Steward = require('src/steward')
Steward.Fault = require('src/fault')

local DATA_DIR = ac.getFolder(ac.FolderID.ACDocuments) .. '/paddock_ladder'
local CONTEXT_PATH = DATA_DIR .. '/race_context.json'
local RESULT_PATH = DATA_DIR .. '/race_result.json'
local PROGRESS_PATH = DATA_DIR .. '/race_progress.json'
local DEBRIEF_PATH = DATA_DIR .. '/race_debrief.json'
local COMMAND_PATH = DATA_DIR .. '/race_command.json'
local WINDOW_NAME = 'IMGUI_LUA_Paddock Ladder_main'

local prefs = ac.storage({ tyresOut = 3, screen = 2, autoOpen = true, placedRight = false, minimised = false })
local steward = Steward.new(prefs.tyresOut)
local nextScreen = ac.ControlButton('paddockladder/Timing: next screen')
local timing = { laps = {}, lastLap = nil, pendingLap = nil, board = {}, boardAge = 1, traces = {}, clock = 0, incidents = {}, lastContact = {} }

local shortcutHeld, screenCooldown = false, 0

local function cycleScreen()
  if screenCooldown > 0 then return end
  screenCooldown = 0.25
  prefs.screen = prefs.screen % 4 + 1
end

nextScreen:setAlwaysActive(true)
nextScreen:onPressed(cycleScreen)

-- Shift+T always works. A wheel button can also be assigned on the Setup screen.
local function checkShortcut(dt)
  screenCooldown = math.max(0, screenCooldown - dt)
  local held = ac.isKeyDown(ui.KeyIndex.T) and ac.isKeyDown(ui.KeyIndex.Shift)
  if held and not shortcutHeld then cycleScreen() end
  shortcutHeld = held
end

local context = nil
local recorded = false
local contacts = {}
local contactTotal = 0
local contactCooldown = 0
local teammateIndex = nil
local windowHandled, sinceLoad = false, 0
local startPosition = nil
local practiceLaps = 0
local lastSessionIndex = nil

local function loadContext()
  local text = io.load(CONTEXT_PATH)
  if not text then return nil end
  local ok, data = pcall(JSON.parse, text)
  if not ok or type(data) ~= 'table' or not data.RaceId then return nil end

  local sim = ac.getSim()
  local matches = not sim.isOnlineRace
    and ac.getTrackID() == data.Track
    and ac.getTrackLayout() == (data.Layout or '')
    and ac.getCarID(0) == data.Car
    and sim.carsCount == data.Cars
    and ac.getDriverName(1) == data.FirstRival
  if not matches then return nil end

  for i = 1, sim.carsCount - 1 do
    if ac.getDriverName(i) == data.Teammate then teammateIndex = i end
  end
  ac.log('[PaddockLadder] career race recognised: ' .. tostring(data.RaceId))
  return data
end

context = loadContext()

local function isRaceRunning()
  local sim = ac.getSim()
  if not context or sim.isReplayActive then return false end
  local session = ac.getSession(sim.currentSessionIndex)
  return session ~= nil and session.type == ac.SessionType.Race
end

local function nearestRival(car)
  local bestIndex, bestDistance = nil, 64
  for _, other in ac.iterateCars() do
    if other.index ~= 0 then
      local dx = other.position.x - car.position.x
      local dy = other.position.y - car.position.y
      local dz = other.position.z - car.position.z
      local distance = dx * dx + dy * dy + dz * dz
      if distance < bestDistance then
        bestIndex, bestDistance = other.index, distance
      end
    end
  end
  return bestIndex
end

-- Speeds at the moment of a crash have already changed, so rulings use where everyone was a moment before.
function timing.noteStates(dt)
  timing.stateAge = (timing.stateAge or 0) + dt
  if timing.stateAge < 0.06 then return end
  timing.stateAge = 0
  timing.olderStates, timing.newerStates = timing.newerStates, {}
  for _, c in ac.iterateCars() do
    timing.newerStates[c.index] = { x = c.position.x, z = c.position.z, vx = c.velocity.x, vz = c.velocity.z, lookX = c.look.x, lookZ = c.look.z, sideX = c.side.x, sideZ = c.side.z }
  end
end

function timing.stateBefore(index)
  return (timing.olderStates and timing.olderStates[index]) or (timing.newerStates and timing.newerStates[index])
end

-- A knock and everything that follows from it in the next few seconds is one incident.
function timing.judgeContact(car)
  local index = nearestRival(car)
  if not index then return end
  local name = ac.getDriverName(index)
  if timing.clock - (timing.lastContact[name] or -99) < 3 then return end
  local mine, theirs = timing.stateBefore(0), timing.stateBefore(index)
  if not mine or not theirs then return end
  timing.lastContact[name] = timing.clock

  local who, kind = Steward.Fault.rule(mine, theirs, car.wheelsOutside)
  timing.incidents[#timing.incidents + 1] = { Driver = name, Who = who, Kind = kind, Lap = car.lapCount + 1 }
  if who == 'you' then
    contacts[name] = (contacts[name] or 0) + 1
    contactTotal = contactTotal + 1
    ac.setMessage('Stewards', 'Your fault: contact with ' .. tostring(name), 'illegal', 3)
  elseif who == 'them' then
    ac.setMessage('Stewards', tostring(name) .. ' hit you. No penalty.', nil, 3)
  else
    ac.setMessage('Stewards', 'Racing incident with ' .. tostring(name) .. '. No penalty.', nil, 3)
  end
end

-- Once the player takes the flag the car goes to its pit box for good, and the results screen takes over.
local FLAG_SECONDS, PARK_CHECK_SECONDS, DEBRIEF_POLL_SECONDS, LAUNCHER_PATIENCE_SECONDS, LEAVE_SECONDS = 2, 1, 0.5, 12, 0.4
local CLICK_GAP_SECONDS, REPLY_POLL_SECONDS, REPLY_PATIENCE_SECONDS = 0.3, 0.15, 10
local finish = nil
local finishHUD -- defined with the drawing code below
local sendCommand -- defined with the drawing code below
local commandSeq, hudRegistered = 0, false

-- Takes over the game's own screens whenever finish is set, and hands them back when it isn't.
local function showFinishScreens()
  if hudRegistered then return end
  hudRegistered = true
  ui.onExclusiveHUD(finishHUD)
end

-- Damage across the weekend --------------------------------------------------------------------

local WEAR_SETTLE_SECONDS, WEAR_RESTORE_SECONDS, PROGRESS_SECONDS = 1, 0.5, 2
local WHEELS = { 0, 1, 2, 3 }
local wear = nil
local carriedSession, lastGear, steeringPull = nil, nil, 0
local raceUnderway = false

local function copyOfFour(values)
  return { values[1], values[2], values[3], values[4] }
end

local function suspensionReadings(car)
  local readings = {}
  for i, wheel in ipairs(WHEELS) do readings[i] = car.wheels[wheel].suspensionDamage end
  return readings
end

-- Starts counting from what the car brought into this session.
local function startWear(car)
  local corners = context.CarSuspension or { 0, 0, 0, 0 }
  wear = {
    body = copyOfFour(context.CarBody),
    engine = context.CarEngine,
    gearbox = context.CarGearbox or 0,
    suspension = copyOfFour(corners),
    carriedGearbox = context.CarGearbox or 0,
    carriedSuspension = copyOfFour(corners),
    startGearbox = car.gearboxDamage,
    startSuspension = suspensionReadings(car),
    settle = WEAR_SETTLE_SECONDS,
    restore = 0,
  }
end

-- Outside the race nothing repairs the car for free, so returning to the pits can't wipe the damage.
-- In the race a real pit stop can, at the cost of the time it takes.
local function updateWear(dt, car)
  if not wear then return end
  if wear.settle > 0 then
    wear.settle = wear.settle - dt
    return
  end

  local raceOn, undone = isRaceRunning(), false
  for i = 1, 4 do
    local now = car.damage[i - 1]
    if raceOn then wear.body[i] = now
    elseif now < wear.body[i] - 0.5 then undone = true
    else wear.body[i] = math.max(wear.body[i], now) end
  end
  if raceOn then wear.engine = car.engineLifeLeft
  elseif car.engineLifeLeft > wear.engine + 1 then undone = true
  else wear.engine = math.min(wear.engine, car.engineLifeLeft) end

  wear.gearbox = math.max(wear.gearbox, wear.carriedGearbox + math.max(0, car.gearboxDamage - wear.startGearbox))
  local corners = suspensionReadings(car)
  for i = 1, 4 do
    wear.suspension[i] = math.max(wear.suspension[i], wear.carriedSuspension[i] + math.max(0, corners[i] - wear.startSuspension[i]))
  end

  wear.restore = wear.restore - dt
  if undone and wear.restore <= 0 then
    wear.restore = WEAR_RESTORE_SECONDS
    physics.setCarBodyDamage(0, vec4(wear.body[1], wear.body[2], wear.body[3], wear.body[4]))
    physics.setCarEngineLife(0, wear.engine)
  end
end

-- What the car carries into whatever comes next.
local function adoptWear()
  if not wear or not context or not context.CarBody then return end
  context.CarBody = copyOfFour(wear.body)
  context.CarEngine = wear.engine
  context.CarGearbox = wear.gearbox
  context.CarSuspension = copyOfFour(wear.suspension)
end

local function carHasDamage()
  local body, corners = context.CarBody, context.CarSuspension or { 0, 0, 0, 0 }
  return body[1] + body[2] + body[3] + body[4] > 0.5 or context.CarEngine < 999 or (context.CarGearbox or 0) > 0.001
    or corners[1] + corners[2] + corners[3] + corners[4] > 0.001
end

local function sessionKind()
  local session = ac.getSession(ac.getSim().currentSessionIndex)
  if session and session.type == ac.SessionType.Race then return 'race' end
  if session and session.type == ac.SessionType.Qualify then return 'qualifying' end
  return 'practice'
end

-- Everything that happened so far. Saved as the race goes on too, so closing the game doesn't lose it.
local function buildResult(car, finished)
  local rows = {}
  for _, c in ac.iterateCars() do
    rows[#rows + 1] = {
      Name = ac.getDriverName(c.index),
      Pos = c.racePosition,
      BestMs = c.bestLapTimeMs,
      IsPlayer = c.index == 0,
    }
  end

  local result = {
    RaceId = context.RaceId,
    Finished = finished,
    Session = sessionKind(),
    RaceStarted = raceUnderway,
    Cuts = isRaceRunning() and steward.sessionCuts or 0,
    Laps = car.lapCount,
    StartPos = startPosition or car.racePosition,
    PracticeLaps = practiceLaps,
    Rows = rows,
  }
  if wear then
    result.Body = copyOfFour(wear.body)
    result.EngineLife = wear.engine
    result.Gearbox = wear.gearbox
    result.Suspension = copyOfFour(wear.suspension)
  end
  local list = {}
  for name, count in pairs(contacts) do list[#list + 1] = { Driver = name, Count = count } end
  if #list > 0 then result.Contacts = list end
  if #timing.incidents > 0 then result.Incidents = timing.incidents end
  return result
end

local function saveProgressNow(car)
  io.createFileDir(PROGRESS_PATH)
  io.save(PROGRESS_PATH, JSON.stringify(buildResult(car, false)), true)
end

local progressClock, wasPaused = 0, false

-- Pausing saves straight away, because that's usually how a player leaves the race.
local function saveProgress(dt, sim, car)
  if not context or recorded then return end
  progressClock = progressClock - dt
  if progressClock > 0 and sim.isPaused == wasPaused then return end
  progressClock = PROGRESS_SECONDS
  wasPaused = sim.isPaused
  saveProgressNow(car)
end

local function writeResult(car)
  recorded = true
  local result = buildResult(car, true)
  io.createFileDir(RESULT_PATH)
  finish = { clock = 0, stepClock = 0, parkCheck = 0, poll = 0, clickWait = 0, page = 'result', listPages = {}, sounds = {}, soundCount = 0, position = car.racePosition, cars = #result.Rows, saved = io.save(RESULT_PATH, JSON.stringify(result), true) }
  showFinishScreens()
end

-- Practice and qualifying never count for the race, though damage stays on the car from one session to the next.
local function resetForNewSession()
  if finish and finish.kind ~= 'session' then return end
  adoptWear()
  carriedSession = nil
  raceUnderway = false
  steward:reset()
  recorded = false
  contacts = {}
  contactTotal = 0
  startPosition = nil
  timing.laps = {}
  timing.lastLap = nil
  timing.pendingLap = nil
  timing.traces = {}
  timing.clock = 0
  timing.incidents = {}
  timing.lastContact = {}
end

ac.onSessionStart(resetForNewSession)

local TRACE_STEPS = 500

-- Distance driven in laps. Cars on the grid sit just before the line, so they count as slightly negative.
local function progressOf(car)
  local spline = car.splinePosition
  if car.lapCount == 0 and spline > 0.5 then spline = spline - 1 end
  return car.lapCount + spline
end

-- Records when each car passes each point on the lap, which is how real timing gaps are measured.
local function updateTraces(dt)
  timing.clock = timing.clock + dt
  for _, c in ac.iterateCars() do
    local step = math.floor(progressOf(c) * TRACE_STEPS)
    local trace = timing.traces[c.index]
    if not trace then
      trace = { last = step, times = {} }
      timing.traces[c.index] = trace
    elseif step > trace.last then
      for s = math.max(trace.last + 1, step - 40), step do
        trace.times[s] = timing.clock
        trace.times[s - 2 * TRACE_STEPS] = nil
      end
      trace.last = step
    end
  end
end

-- The lap time settles just after the line, so each lap is logged a moment later.
local function trackLaps(dt, car)
  if timing.lastLap == nil then timing.lastLap = car.lapCount end
  if car.lapCount > timing.lastLap then
    timing.lastLap = car.lapCount
    timing.pendingLap = { number = car.lapCount, cuts = steward.lapCuts, wait = 0.15 }
  elseif car.lapCount < timing.lastLap then
    timing.lastLap = car.lapCount
  end

  local pending = timing.pendingLap
  if pending then
    pending.wait = pending.wait - dt
    if pending.wait <= 0 then
      timing.pendingLap = nil
      if car.previousLapTimeMs > 0 then
        table.insert(timing.laps, 1, { number = pending.number, ms = car.previousLapTimeMs, valid = car.isLastLapValid, cuts = pending.cuts, position = car.racePosition })
      end
    end
  end
end

-- Career races bring the window up by themselves, once, and the first time it goes top right.
local function openForCareerRace(dt)
  if windowHandled or not context or not prefs.autoOpen then return end
  sinceLoad = sinceLoad + dt
  if sinceLoad < 1.5 then return end
  windowHandled = true

  local window = ac.accessAppWindow(WINDOW_NAME)
  if not window or not window:valid() then return end
  if not window:visible() then window:setVisible(true) end
  if not prefs.placedRight then
    prefs.placedRight = true
    window:move(vec2(ac.getUI().windowSize.x - window:size().x - 30, 90))
  end
end

-- Pure reads its settings as the race loads, so the player's own settings can go straight back afterwards.
local PURE_SETTINGS = ac.getFolder(ac.FolderID.ExtCfgState) .. '/lua/wfx_controller/pureCtrl__settings.ini'
local PURE_BACKUP = DATA_DIR .. '/pure_settings.backup'
local pureRestored, sinceStart = context == nil, 0

local function restorePureSettings(dt)
  if pureRestored then return end
  sinceStart = sinceStart + dt
  if sinceStart < 4 then return end
  pureRestored = true
  local saved = io.load(PURE_BACKUP)
  if saved == nil then return end
  if io.save(PURE_SETTINGS, saved, true) then io.deleteFile(PURE_BACKUP) end
end

-- With practice on and qualifying off, a hidden qualifying session puts the player at the back of the grid.
local skipWait = 0

local function skipHiddenQualifying(dt, sim)
  if not context or not context.SkipQualifying then return end
  local session = ac.getSession(sim.currentSessionIndex)
  if not session or session.type ~= ac.SessionType.Qualify then return end
  skipWait = skipWait - dt
  if skipWait <= 0 then
    skipWait = 1
    ac.tryToSkipSession()
  end
end

-- Carried damage --------------------------------------------------------------------------------

local MAX_STEERING_PULL, MAX_CORNER_GRIP_LOSS = 0.035, 0.12

-- AC can load body and engine damage directly. Gearbox and suspension can't be loaded,
-- so their effects are applied instead: slower shifts, a pull to one side and less grip at that corner.
local function applyCarriedDamage(sim, car)
  if carriedSession == sim.currentSessionIndex or not context.CarBody then return end
  carriedSession = sim.currentSessionIndex

  local body = context.CarBody
  physics.setCarBodyDamage(0, vec4(body[1], body[2], body[3], body[4]))
  physics.setCarEngineLife(0, context.CarEngine)
  startWear(car)

  local corners = context.CarSuspension or { 0, 0, 0, 0 }
  local wheels = { ac.Wheel.FrontLeft, ac.Wheel.FrontRight, ac.Wheel.RearLeft, ac.Wheel.RearRight }
  for i, wheel in ipairs(wheels) do
    physics.setGripDecrease(0, wheel, math.min(1, corners[i]) * MAX_CORNER_GRIP_LOSS)
  end
  steeringPull = math.max(-1, math.min(1, corners[1] + corners[3] - corners[2] - corners[4])) * MAX_STEERING_PULL
end

local function applyGearboxWear(car)
  local wear = context.CarGearbox or 0
  if wear > 0 and lastGear and car.gear ~= lastGear and car.gear > 0 then
    physics.lockUserGearboxFor(0.2 + wear * 0.8)
    -- Holding the clutch invalidates the lap, so qualifying laps aren't touched.
    if isRaceRunning() then physics.forceUserClutchFor(0.08 + wear * 0.45, 0) end
  end
  lastGear = car.gear
end

-- With damage off, knocks are undone during the race so they can't slow you down.
local damageClearClock = 0

local function clearDamageIfOff(dt, car)
  damageClearClock = damageClearClock - dt
  if damageClearClock > 0 or not isRaceRunning() then return end
  damageClearClock = 0.5
  if car.damage[0] + car.damage[1] + car.damage[2] + car.damage[3] > 0 then physics.setCarBodyDamage(0, vec4(0, 0, 0, 0)) end
  if car.engineLifeLeft < 1000 then physics.setCarEngineLife(0, 1000) end
end

-- A grudge only shows when you're close enough to matter, so the rest of the grid isn't affected.
local RIVAL_RANGE_SQUARED = 40 * 40
local rivalIndexes, rivalClock = nil, 0

local function updateRivalries(dt, car)
  rivalClock = rivalClock - dt
  local shift = context.RespectShift or 0
  if rivalClock > 0 or ((not context.Rivalries or #context.Rivalries == 0) and shift == 0) or not isRaceRunning() then return end
  rivalClock = 0.4
  if not rivalIndexes then
    rivalIndexes = {}
    for i = 1, ac.getSim().carsCount - 1 do
      local known = nil
      for _, rival in ipairs(context.Rivalries or {}) do
        if ac.getDriverName(i) == rival.Name then known = rival end
      end
      -- Everyone else reacts to your reputation instead of a personal grudge.
      local other = ac.getCar(i)
      if not known and shift ~= 0 and other and other.aiAggression >= 0 then
        local normal = other.aiAggression / 0.95 * 100
        known = { Normal = normal, Angry = math.max(0, math.min(100, normal + shift)) }
      end
      if known then rivalIndexes[#rivalIndexes + 1] = { index = i, rival = known, angry = false } end
    end
  end
  for _, entry in ipairs(rivalIndexes) do
    local other = ac.getCar(entry.index)
    if other then
      local dx, dz = other.position.x - car.position.x, other.position.z - car.position.z
      local angry = dx * dx + dz * dz < RIVAL_RANGE_SQUARED
      if angry ~= entry.angry then
        entry.angry = angry
        physics.setAIAggression(entry.index, (angry and entry.rival.Angry or entry.rival.Normal) / 100 * 0.95)
      end
    end
  end
end

local function updateCareerCar(dt, sim, car)
  if not context then return end
  if context.DamageOff then clearDamageIfOff(dt, car) else
    applyCarriedDamage(sim, car)
    updateWear(dt, car)
    applyGearboxWear(car)
    if steeringPull ~= 0 then physics.offsetUserSteering(steeringPull) end
  end
  updateRivalries(dt, car)
end

local function keepCarParked(car)
  if not car.isInPit and not ac.tryToTeleportToPits() then physics.teleportCarTo(0, ac.SpawnSet.Pits) end
  physics.setCarNoInput(true)
end

local function readDebrief()
  local text = io.load(DEBRIEF_PATH)
  if not text then return nil end
  local ok, data = pcall(JSON.parse, text)
  if ok and type(data) == 'table' and data.RaceId == context.RaceId and (data.SessionReview == true) == (finish.kind == 'session') then return data end
  return nil
end

local function closeGame(reason)
  finish.leaving = reason
  finish.leaveIn = LEAVE_SECONDS
end

-- The launcher answers each choice made on the results screen by writing the debrief again.
local function checkForReply(dt)
  local waiting = finish.waiting
  waiting.clock = waiting.clock + dt
  waiting.poll = waiting.poll - dt
  if waiting.poll > 0 then return end
  waiting.poll = REPLY_POLL_SECONDS

  local data = readDebrief()
  if data and data.Revision == waiting.seq then
    finish.waiting = nil
    finish.debrief = data
    if data.SessionReview and data.CarBody then
      -- The launcher now holds the damage, repairs included, so this replaces what the car was counting.
      context.CarBody, context.CarEngine, context.CarGearbox, context.CarSuspension = data.CarBody, data.CarEngine, data.CarGearbox, data.CarSuspension
      context.Credits = data.CreditsBalance
      wear = nil
    end
    if data.Signed then
      finish.page, finish.stepClock = 'next', 0
      return
    end
    if waiting.after then waiting.after(data) end
    if data.ReplyTitle ~= '' then finish.modal = { title = data.ReplyTitle, text = data.ReplyText } end
  elseif waiting.clock > REPLY_PATIENCE_SECONDS then
    finish.waiting = nil
    if finish.kind == 'session' then finish.noReply = true
    else finish.modal = { title = 'No answer from Paddock Ladder', text = 'Check the launcher is still open, then try again.' } end
  end
end

local MUSIC_PATH = ac.getFolder(ac.FolderID.Root) .. '/apps/lua/PaddockLadder/music/menu.mp3'
local MUSIC_FADE_PER_SECOND = 0.25

-- The launcher's music setting arrives with the debrief, so the track starts once that's here.
local function updateFinishMusic(dt)
  local d = finish.debrief
  if not d or not d.MusicOn then return end
  if not finish.music then
    if not io.fileExists(MUSIC_PATH) then return end
    finish.music = ui.MediaPlayer(MUSIC_PATH):setAutoPlay(true):setLooping(true):setVolume(0)
    finish.musicLevel = 0
  end
  local target = finish.leaving and 0 or d.MusicVolume / 100
  local step = MUSIC_FADE_PER_SECOND * dt * (finish.leaving and 4 or 1)
  finish.musicLevel = finish.musicLevel < target and math.min(target, finish.musicLevel + step) or math.max(target, finish.musicLevel - step)
  finish.music:setVolume(finish.musicLevel)
  ac.store('paddockladder.musicTime', finish.music:currentTime()) -- read by the real-game test
end

local function reportScreenForTest()
  ac.store('paddockladder.screen', finish.leaving and 'leaving' or finish.modal and 'modal' or finish.page)
  for _, sound in pairs(finish.sounds) do
    if sound:isPlaying() then finish.soundHeard = 1 end
  end
  ac.store('paddockladder.soundHeard', finish.soundHeard or 0)
  ac.store('paddockladder.soundCount', finish.soundCount)
end

local reviewedSession = nil

local function closeSessionReview()
  local review, sim = finish, ac.getSim()
  finish = nil
  carriedSession = nil
  reviewedSession = review.sessionIndex
  ac.store('paddockladder.screen', '')
  if review.skipAfter and sim.currentSessionIndex == review.sessionIndex then ac.tryToSkipSession() end
  lastSessionIndex = review.skipAfter and review.sessionIndex or sim.currentSessionIndex
end

local function nextSessionLabel(index)
  local session = ac.getSession(index)
  if session and session.type == ac.SessionType.Qualify and context.SkipQualifying then session = ac.getSession(index + 1) end
  if session and session.type == ac.SessionType.Qualify then return 'CONTINUE TO QUALIFYING' end
  if session and session.type == ac.SessionType.Race then return 'CONTINUE TO RACE' end
  return 'CONTINUE'
end

-- At the end of practice and qualifying a damaged car goes to the garage screen before the next session.
local function beginSessionReview(sessionIndex, skipAfter, car)
  if not context or context.DamageOff or not context.CarBody or finish then return false end
  local session = ac.getSession(sessionIndex)
  if not session or session.type == ac.SessionType.Race then return false end
  if session.type == ac.SessionType.Qualify and context.SkipQualifying then return false end
  adoptWear()
  if not carHasDamage() then return false end

  saveProgressNow(car)
  finish = {
    kind = 'session', title = (session.type == ac.SessionType.Qualify and 'QUALIFYING' or 'PRACTICE') .. ' COMPLETE',
    sessionIndex = sessionIndex, skipAfter = skipAfter,
    nextLabel = nextSessionLabel(skipAfter and sessionIndex + 1 or ac.getSim().currentSessionIndex),
    clock = FLAG_SECONDS, parkCheck = 0, poll = 0, clickWait = CLICK_GAP_SECONDS, page = 'garage', listPages = {}, sounds = {}, soundCount = 0,
    position = 1, cars = 1, saved = true, stepClock = 0,
  }
  sendCommand('session')
  showFinishScreens()
  return true
end

local function updateFinish(dt, car)
  if finish.closeReview then
    closeSessionReview()
    return
  end
  finish.clock = finish.clock + dt
  finish.stepClock = (finish.stepClock or 0) + dt
  reportScreenForTest()
  local musicOk, musicError = pcall(updateFinishMusic, dt)
  if not musicOk and not finish.musicFailed then
    finish.musicFailed = true
    ac.error('[PaddockLadder] music stopped: ' .. tostring(musicError))
  end
  finish.clickWait = math.max(0, finish.clickWait - dt)
  if finish.waiting then checkForReply(dt) end

  -- Checked again every second, so even restarting the session from a keyboard shortcut can't free the car.
  if finish.clock >= FLAG_SECONDS and finish.kind ~= 'session' then
    finish.parkCheck = finish.parkCheck - dt
    if finish.parkCheck <= 0 then
      finish.parkCheck = PARK_CHECK_SECONDS
      local ok, err = pcall(keepCarParked, car)
      if not ok then ac.error('[PaddockLadder] could not park the car: ' .. tostring(err)) end
    end
  end

  if not finish.debrief and finish.kind ~= 'session' then
    finish.poll = finish.poll - dt
    if finish.poll <= 0 then
      finish.poll = DEBRIEF_POLL_SECONDS
      finish.debrief = readDebrief()
    end
  end

  if finish.leaving and not finish.closing then
    finish.leaveIn = finish.leaveIn - dt
    if finish.leaveIn <= 0 then
      finish.closing = true
      ac.shutdownAssettoCorsa()
    end
  end
end

local carFailed, progressFailed = false, false

function script.update(dt)
  restorePureSettings(dt)
  local sim, car = ac.getSim(), ac.getCar(0)
  if car and finish then
    updateFinish(dt, car)
    return
  end
  if not car or sim.isReplayActive then return end
  if lastSessionIndex ~= sim.currentSessionIndex then
    local previous = lastSessionIndex
    if previous ~= nil then resetForNewSession() end
    lastSessionIndex = sim.currentSessionIndex
    if previous ~= nil and previous ~= reviewedSession and beginSessionReview(previous, false, car) then return end
  end
  skipHiddenQualifying(dt, sim)

  local progressOk, progressError = pcall(saveProgress, dt, sim, car)
  if not progressOk and not progressFailed then
    progressFailed = true
    ac.error('[PaddockLadder] progress could not be saved: ' .. tostring(progressError))
  end
  if sim.isInMainMenu then return end

  local careerOk, careerError = pcall(updateCareerCar, dt, sim, car)
  if not careerOk and not carFailed then
    carFailed = true
    ac.error('[PaddockLadder] career car effects stopped: ' .. tostring(careerError))
  end

  openForCareerRace(dt)
  checkShortcut(dt)
  trackLaps(dt, car)
  updateTraces(dt)

  local cut = steward:update(dt, {
    wheelsOutside = car.wheelsOutside,
    speedKmh = car.speedKmh,
    inPitlane = car.isInPitlane,
    spline = car.splinePosition,
    lapCount = car.lapCount,
  })
  if context and ac.getSession(sim.currentSessionIndex) and ac.getSession(sim.currentSessionIndex).type == ac.SessionType.Practice then
    practiceLaps = math.max(practiceLaps, steward.bestClean)
  end
  if cut and steward.lapCuts >= 2 and not context then
    ac.setMessage('Track limits', string.format('%d cuts this lap', steward.lapCuts), 'illegal', 2.5)
  end

  if recorded or not isRaceRunning() then return end

  -- The grid slot is only final once the lights go out.
  if startPosition == nil or not sim.isSessionStarted then startPosition = car.racePosition end
  if sim.isSessionStarted then raceUnderway = true end

  timing.noteStates(dt)
  contactCooldown = math.max(0, contactCooldown - dt)
  if car.collidedWith > 0 and car.collisionDepth > 0 and contactCooldown == 0 then
    contactCooldown = 0.5
    timing.judgeContact(car)
  end

  if car.isRaceFinished then writeResult(car) end
end

-- Drawing helpers ------------------------------------------------------------------------------

local Weight = ui.DWriteFont.Weight

local function makeFont(weight, tracking)
  local font = ui.DWriteFont('Bahnschrift'):weight(weight)
  if tracking and font.spacing then font = font:spacing(0, tracking, 0) end
  return font
end

local FONT_LABEL = makeFont(Weight.SemiBold, 1.1)
local FONT_VALUE = makeFont(Weight.Regular)
local FONT_TITLE = makeFont(Weight.Light)

local INK = { 0.035, 0.04, 0.05 }
local TEXT = { 0.95, 0.94, 0.92 }
local MUTED = { 0.56, 0.58, 0.62 }
local VOLT = { 0.9, 1, 0.34 }
local GOOD = { 0.44, 0.89, 0.6 }
local WARN = { 1, 0.75, 0.3 }
local BAD = { 1, 0.42, 0.37 }
local STATE_COLOR = { ok = GOOD, warn = WARN, bad = BAD }
local STATE_RANK = { ok = 0, warn = 1, bad = 2 }

local function paint(color, alpha)
  return rgbm(color[1], color[2], color[3], alpha)
end

local function drawLeft(text, size, x, y, color, font)
  ui.pushDWriteFont(font)
  ui.dwriteDrawText(text, size, vec2(x, y), color)
  ui.popDWriteFont()
end

local function drawRight(text, size, rightX, y, color, font)
  ui.pushDWriteFont(font)
  ui.dwriteDrawText(text, size, vec2(rightX - ui.measureDWriteText(text, size).x, y), color)
  ui.popDWriteFont()
end

local function measure(text, size, font)
  ui.pushDWriteFont(font)
  local width = ui.measureDWriteText(text, size).x
  ui.popDWriteFont()
  return width
end

local function hairline(left, right, y)
  ui.drawRectFilled(vec2(left, y), vec2(right, y + 1), rgbm(1, 1, 1, 0.1))
end

-- Contract and steward status ------------------------------------------------------------------

local flashes, lastValues = {}, {}

local function limitCell(label, used, limit)
  local state = used > limit and 'bad' or (used == limit and limit > 0) and 'warn' or 'ok'
  local fraction = limit > 0 and math.min(1, used / limit) or (used > 0 and 1 or 0)
  return { label = label, value = string.format('%d / %d', used, limit), state = state, meter = fraction }
end

local function contractCell(clause, car)
  if clause.Kind == 'maxCuts' then
    return limitCell('TRACK LIMITS', steward.sessionCuts, clause.Target)
  elseif clause.Kind == 'maxContacts' then
    return limitCell('CONTACT', contactTotal, clause.Target)
  elseif clause.Kind == 'finishTop' then
    return { label = 'TARGET TOP ' .. clause.Target, value = 'P' .. car.racePosition, state = car.racePosition <= clause.Target and 'ok' or 'bad' }
  elseif clause.Kind == 'beatTeammate' and teammateIndex then
    local mate = ac.getCar(teammateIndex)
    local gap = mate and (mate.racePosition - car.racePosition) or 0
    return { label = 'VS TEAMMATE', value = gap > 0 and '+' .. gap or tostring(gap), state = gap > 0 and 'ok' or 'bad' }
  end
  return nil
end

local function statusCells(car, career)
  local cells = {}
  if career then
    for _, clause in ipairs(context.Clauses or {}) do
      local cell = contractCell(clause, car)
      if cell then cells[#cells + 1] = cell end
    end
  else
    cells[1] = { label = 'CUTS THIS LAP', value = tostring(steward.lapCuts), state = steward.lapCuts == 0 and 'ok' or 'bad' }
    cells[2] = { label = 'SESSION CUTS', value = tostring(steward.sessionCuts), state = 'ok' }
  end
  cells[#cells + 1] = { label = 'CLEAN LAPS', value = tostring(steward.cleanLaps), state = 'ok' }
  return cells
end

local function credits(amount)
  local text = tostring(math.floor(amount or 0))
  while true do
    local changed, count = text:gsub('^(-?%d+)(%d%d%d)', '%1,%2')
    text = changed
    if count == 0 then return text end
  end
end

-- What this practice or qualifying session is paying for, if anything.
local function sessionGoal(car, name)
  if name == 'PRACTICE' and context.PracticeGoalLaps > 0 then
    local done = math.max(steward.bestClean, steward.cleanLaps)
    return { label = 'GOAL LAPS', value = math.min(done, context.PracticeGoalLaps) .. ' / ' .. context.PracticeGoalLaps, state = done >= context.PracticeGoalLaps and 'ok' or 'warn' }, context.PracticeReward
  elseif name == 'QUALIFYING' and context.QualifyTop > 0 and not context.SkipQualifying then
    return { label = 'GOAL TOP ' .. context.QualifyTop, value = car.bestLapTimeMs > 0 and 'P' .. car.racePosition or 'NO LAP', state = (car.bestLapTimeMs > 0 and car.racePosition <= context.QualifyTop) and 'ok' or 'warn' }, context.QualifyReward
  end
  return nil, 0
end

local function rivalNote(car)
  if not context.Notes then return nil end
  for _, other in ac.iterateCars() do
    if other.index ~= 0 and math.abs(other.racePosition - car.racePosition) == 1 then
      local name = ac.getDriverName(other.index)
      local note = context.Notes[name]
      if note then return (tostring(name):match('(%S+)%s*$') or tostring(name)):upper(), note end
    end
  end
  return nil
end

-- The name of the current session, or nil during the race itself.
local function sessionName()
  local session = ac.getSession(ac.getSim().currentSessionIndex)
  if not session then return nil end
  if session.type == ac.SessionType.Practice then return 'PRACTICE' end
  if session.type == ac.SessionType.Qualify then return 'QUALIFYING' end
  return nil
end

-- Draws the status band and returns where the screen below it should start, plus the worst state.
local function drawStatus(W, s, y, car, dt)
  local career = context ~= nil and isRaceRunning()
  local name = sessionName()
  local title = career and ('CONTRACT  /  ' .. tostring(context.Team or context.SeriesName or ''):upper())
    or (context and name) and (select(1, sessionGoal(car, name)) and (name .. '  /  +' .. credits(select(2, sessionGoal(car, name))) .. ' CR') or (name .. '  /  NOT SCORED')) or 'TRACK LIMITS'
  drawLeft(title, 9.5 * s, 14 * s, y, paint(MUTED, 1), FONT_LABEL)
  local labelLeft = W - 14 * s
  if career then
    local label = 'ROUND ' .. tostring(context.RoundNumber)
    labelLeft = labelLeft - measure(label, 9.5 * s, FONT_LABEL)
    drawLeft(label, 9.5 * s, labelLeft, y, paint(MUTED, 1), FONT_LABEL)
  end
  if context and name and not ac.getSim().isReplayActive then
    local label = 'SKIP SESSION'
    local left = W - 14 * s - measure(label, 9.5 * s, FONT_LABEL)
    labelLeft = left
    drawLeft(label, 9.5 * s, left, y, paint(VOLT, 1), FONT_LABEL)
    ui.setCursor(vec2(left - 6 * s, y - 5 * s))
    if ui.invisibleButton('##skipSession', vec2(W - 14 * s - left + 12 * s, 22 * s)) then
      if not beginSessionReview(ac.getSim().currentSessionIndex, true, car) then ac.tryToSkipSession() end
    end
  end
  if context and context.Credits then drawRight(credits(context.Credits) .. ' CR', 9.5 * s, labelLeft - 14 * s, y, paint(context.Credits < 0 and BAD or VOLT, 1), FONT_LABEL) end
  y = y + 18 * s

  local cells = statusCells(car, career)
  if context and name then
    local goal = sessionGoal(car, name)
    if goal then table.insert(cells, 1, goal) end
  end
  local worst = 'ok'
  local cellWidth = (W - 28 * s) / #cells
  local cellHeight = 46 * s
  for i, cell in ipairs(cells) do
    if lastValues[cell.label] ~= nil and lastValues[cell.label] ~= cell.value then flashes[cell.label] = 1 end
    lastValues[cell.label] = cell.value
    flashes[cell.label] = math.max(0, (flashes[cell.label] or 0) - dt * 1.4)
    if STATE_RANK[cell.state] > STATE_RANK[worst] then worst = cell.state end

    local left = 14 * s + (i - 1) * cellWidth
    local right = left + cellWidth - 10 * s
    local color = STATE_COLOR[cell.state]
    if flashes[cell.label] > 0 then
      ui.drawRectFilled(vec2(left - 4 * s, y - 3 * s), vec2(right + 4 * s, y + cellHeight - 3 * s), paint(color, 0.24 * flashes[cell.label]))
    end
    drawLeft(cell.label, 9.5 * s, left, y, paint(MUTED, 1), FONT_LABEL)
    drawLeft(cell.value, 20 * s, left, y + 12 * s, paint(cell.state == 'ok' and TEXT or color, 1), FONT_VALUE)

    local barTop = y + 39 * s
    ui.drawRectFilled(vec2(left, barTop), vec2(right, barTop + 2 * s), rgbm(1, 1, 1, 0.1))
    local fill = cell.meter or 1
    if fill > 0 then ui.drawRectFilled(vec2(left, barTop), vec2(left + (right - left) * fill, barTop + 2 * s), paint(color, cell.meter and 1 or 0.55)) end
  end
  y = y + cellHeight

  -- Where on the lap you've gone wide: start line on the left.
  local stripLeft, stripWidth = 14 * s, W - 28 * s
  ui.drawRectFilled(vec2(stripLeft, y), vec2(stripLeft + stripWidth, y + 3 * s), rgbm(1, 1, 1, 0.08))
  for i = 1, #steward.bins do
    local count = steward.bins[i]
    if count > 0 then
      local x = stripLeft + (i - 1) / #steward.bins * stripWidth
      ui.drawRectFilled(vec2(x, y), vec2(x + math.max(2, stripWidth / #steward.bins), y + 3 * s), paint(BAD, math.min(1, 0.45 + count * 0.18)))
    end
  end
  y = y + 8 * s

  if career then
    local noteName, noteText = rivalNote(car)
    if noteName then
      ui.drawRectFilled(vec2(0, y), vec2(W, y + 25 * s), paint(BAD, 0.16))
      drawLeft(noteName, 9.5 * s, 14 * s, y + 7 * s, paint(BAD, 1), FONT_LABEL)
      drawLeft(noteText, 12.5 * s, 26 * s + measure(noteName, 9.5 * s, FONT_LABEL), y + 5 * s, paint(TEXT, 0.85), FONT_VALUE)
      y = y + 29 * s
    end
  end

  hairline(14 * s, W - 14 * s, y)
  return y, worst
end

-- Timing -----------------------------------------------------------------------------------------

local function isRaceSession()
  local session = ac.getSession(ac.getSim().currentSessionIndex)
  return session ~= nil and session.type == ac.SessionType.Race
end

local function lapText(ms)
  return ms and ms > 0 and ac.lapTimeToString(ms) or '--'
end

-- How long ago the car ahead passed the exact point this car is at now.
local function secondsBehind(row, other)
  local mine, theirs = timing.traces[row.index], timing.traces[other.index]
  if not mine or not theirs then return nil end
  local exact = row.progress * TRACE_STEPS
  local step = math.floor(exact)
  local passed = theirs.times[step]
  if not passed then return nil end
  local following = theirs.times[step + 1]
  if following then passed = passed + (following - passed) * (exact - step) end
  return math.max(0, timing.clock - passed)
end

local function gapText(row, other, inRace)
  local seconds
  if inRace then
    local lapsDown = math.floor(other.progress - row.progress)
    if lapsDown >= 1 then return '+' .. lapsDown .. (lapsDown == 1 and ' LAP' or ' LAPS') end
    seconds = secondsBehind(row, other)
  elseif row.best > 0 and other.best > 0 then
    seconds = (row.best - other.best) / 1000
  end
  return seconds and string.format('%+.3f', seconds) or '--'
end

local function refreshBoard()
  local rows = {}
  for _, c in ac.iterateCars() do
    if c.isConnected then
      rows[#rows + 1] = {
        index = c.index, position = c.racePosition, name = ac.getDriverName(c.index), progress = progressOf(c),
        last = c.previousLapTimeMs, best = c.bestLapTimeMs, pit = c.isInPitlane,
      }
    end
  end
  table.sort(rows, function(a, b) return a.position < b.position end)

  local inRace = isRaceSession()
  for i, row in ipairs(rows) do
    if i == 1 then
      row.gap, row.interval = inRace and 'LEADER' or lapText(row.best), ''
    else
      row.gap = gapText(row, rows[1], inRace)
      row.interval = gapText(row, rows[i - 1], inRace)
    end
  end
  timing.board = rows
end

local TAB_NAMES = { 'LAPS', 'RACE', 'SETUP', 'CAR' }

-- Anything added from here on lives in this table, because Lua allows a file only 200 local names.
local Screens = {}

-- Shared by every screen, so your position is always visible.
local function drawHeader(W, s, car, total)
  local position = 'P' .. car.racePosition
  drawLeft(position, 40 * s, 14 * s, 8 * s, paint(TEXT, 1), FONT_TITLE)
  drawLeft('/ ' .. total, 15 * s, 22 * s + measure(position, 40 * s, FONT_TITLE), 31 * s, paint(MUTED, 1), FONT_VALUE)

  local session = ac.getSession(ac.getSim().currentSessionIndex)
  local lapNow = car.lapCount + 1
  if session and session.laps > 0 then lapNow = math.min(lapNow, session.laps) end
  local lapLabel = session and session.laps > 0 and (lapNow .. ' / ' .. session.laps) or tostring(lapNow)
  drawLeft('LAP', 10 * s, 124 * s, 14 * s, paint(MUTED, 1), FONT_LABEL)
  drawLeft(lapLabel, 20 * s, 124 * s, 27 * s, paint(TEXT, 1), FONT_VALUE)

  if session and (session.isTimedRace or session.type ~= ac.SessionType.Race) then
    local left = math.max(0, math.floor(ac.getSim().sessionTimeLeft / 1000))
    drawLeft('LEFT', 10 * s, 184 * s, 14 * s, paint(MUTED, 1), FONT_LABEL)
    drawLeft(string.format('%d:%02d:%02d', math.floor(left / 3600), math.floor(left / 60) % 60, left % 60), 20 * s, 184 * s, 27 * s, paint(TEXT, 1), FONT_VALUE)
  end

  local right = W - 14 * s
  for i = #TAB_NAMES, 1, -1 do
    local width = measure(TAB_NAMES[i], 10.5 * s, FONT_LABEL)
    local left = right - width
    local active = prefs.screen == i
    drawLeft(TAB_NAMES[i], 10.5 * s, left, 21 * s, paint(active and TEXT or MUTED, 1), FONT_LABEL)
    if active then ui.drawRectFilled(vec2(left, 37 * s), vec2(right, 39 * s), paint(VOLT, 1)) end
    ui.setCursor(vec2(left - 6 * s, 12 * s))
    if ui.invisibleButton('##tab' .. i, vec2(width + 12 * s, 34 * s)) then prefs.screen = i end
    right = left - 16 * s
  end

  -- Minimise, like the bar other apps have.
  -- It sits mid-window because the game's own sidebar slides over the far right of the screen.
  ui.drawRectFilled(vec2(W / 2 - 7 * s, 6 * s), vec2(W / 2 + 7 * s, 8 * s), paint(MUTED, 1))
  ui.setCursor(vec2(W / 2 - 24 * s, 0))
  if ui.invisibleButton('##minimise', vec2(48 * s, 18 * s)) then prefs.minimised = true end

  hairline(14 * s, W - 14 * s, 60 * s)
end

local function drawTimeCell(label, value, x, y, s, color)
  drawLeft(label, 9.5 * s, x, y, paint(MUTED, 1), FONT_LABEL)
  drawLeft(value, 21 * s, x, y + 13 * s, paint(color or TEXT, 1), FONT_VALUE)
end

local function drawLapsPage(W, H, s, car, y0)
  local bestMs = car.bestLapTimeMs
  local third = (W - 28 * s) / 3
  drawTimeCell('CURRENT', lapText(car.lapTimeMs), 14 * s, y0 + 10 * s, s)
  drawTimeCell('LAST', lapText(car.previousLapTimeMs), 14 * s + third, y0 + 10 * s, s)
  drawTimeCell('BEST', lapText(bestMs), 14 * s + 2 * third, y0 + 10 * s, s, VOLT)

  local columns = { 14 * s, 56 * s, 166 * s, 270 * s }
  local top = y0 + 60 * s
  for i, name in ipairs({ 'LAP', 'TIME', 'TO BEST', 'CUTS' }) do
    drawLeft(name, 9.5 * s, columns[i], top, paint(MUTED, 1), FONT_LABEL)
  end
  drawRight('POS', 9.5 * s, W - 14 * s, top, paint(MUTED, 1), FONT_LABEL)
  hairline(14 * s, W - 14 * s, top + 17 * s)

  if #timing.laps == 0 then
    drawLeft('Completed laps appear here.', 13 * s, 14 * s, top + 28 * s, paint(MUTED, 1), FONT_VALUE)
    return
  end

  local rowHeight = 25 * s
  local y = top + 23 * s
  for _, lap in ipairs(timing.laps) do
    if y + rowHeight > H - 4 * s then break end
    local isBest = lap.ms == bestMs and lap.valid
    local color = not lap.valid and MUTED or isBest and VOLT or TEXT
    local delta = bestMs > 0 and lap.ms ~= bestMs and string.format('%+.3f', (lap.ms - bestMs) / 1000) or (lap.valid and '-' or '')
    drawLeft(tostring(lap.number), 14 * s, columns[1], y, paint(MUTED, 1), FONT_VALUE)
    drawLeft(lapText(lap.ms) .. (lap.valid and '' or '  X'), 15 * s, columns[2], y - 1 * s, paint(color, 1), FONT_VALUE)
    drawLeft(delta, 14 * s, columns[3], y, paint(MUTED, 1), FONT_VALUE)
    drawLeft(tostring(lap.cuts), 14 * s, columns[4], y, paint(lap.cuts > 0 and BAD or MUTED, 1), FONT_VALUE)
    drawRight('P' .. lap.position, 14 * s, W - 14 * s, y, paint(TEXT, 1), FONT_VALUE)
    y = y + rowHeight
  end
end

-- Your teammate and main rivals stand out on the timing screen.
function Screens.tagFor(name)
  if not context then return nil end
  if name == context.Teammate then return 'TEAMMATE', GOOD end
  if name == context.ChosenRival then return 'YOUR RIVAL', BAD end
  for _, rival in ipairs(context.MainRivals or {}) do
    if rival == name then return 'RIVAL', BAD end
  end
  return nil
end

local function drawRacePage(W, H, s, y0)
  local board = timing.board
  local columns = { pos = 14 * s, name = 46 * s, gap = W - 244 * s, interval = W - 164 * s, last = W - 84 * s }
  local top = y0 + 8 * s
  drawLeft('DRIVER', 9.5 * s, columns.name, top, paint(MUTED, 1), FONT_LABEL)
  drawLeft('GAP', 9.5 * s, columns.gap, top, paint(MUTED, 1), FONT_LABEL)
  drawLeft('INT', 9.5 * s, columns.interval, top, paint(MUTED, 1), FONT_LABEL)
  drawLeft('LAST', 9.5 * s, columns.last, top, paint(MUTED, 1), FONT_LABEL)
  hairline(14 * s, W - 14 * s, top + 17 * s)

  local rowHeight = 25 * s
  local firstY = top + 22 * s
  local visible = math.max(1, math.floor((H - firstY - 4 * s) / rowHeight))
  local playerRow = 1
  for i, row in ipairs(board) do
    if row.index == 0 then playerRow = i end
  end
  local start = math.max(1, math.min(playerRow - math.floor(visible / 2), #board - visible + 1))

  for i = start, math.min(#board, start + visible - 1) do
    local row = board[i]
    local y = firstY + (i - start) * rowHeight
    local mine = row.index == 0
    if mine then
      ui.drawRectFilled(vec2(0, y - 2 * s), vec2(W, y + rowHeight - 3 * s), rgbm(1, 1, 1, 0.08))
      ui.drawRectFilled(vec2(0, y - 2 * s), vec2(3 * s, y + rowHeight - 3 * s), paint(VOLT, 1))
    end
    drawLeft(tostring(row.position), 14 * s, columns.pos, y, paint(MUTED, 1), FONT_VALUE)
    ui.pushDWriteFont(FONT_VALUE)
    local tag, tagColor = Screens.tagFor(row.name)
    local shownName = row.name .. (tag and '  ' .. tag or '') .. (row.pit and '  PIT' or '')
    ui.dwriteDrawTextClipped(shownName, 14 * s, vec2(columns.name, y), vec2(columns.gap - 6 * s, y + rowHeight), ui.Alignment.Start, ui.Alignment.Start, false, paint(mine and VOLT or tagColor or TEXT, 1))
    ui.popDWriteFont()
    drawLeft(row.gap, 13 * s, columns.gap, y + 1 * s, paint(TEXT, 1), FONT_VALUE)
    drawLeft(row.interval, 13 * s, columns.interval, y + 1 * s, paint(MUTED, 1), FONT_VALUE)
    drawLeft(lapText(row.last), 13 * s, columns.last, y + 1 * s, paint(MUTED, 1), FONT_VALUE)
  end
end

local function drawSetupPage(W, s, y0)
  drawLeft('WHEEL BUTTON FOR NEXT SCREEN  /  OR SHIFT+T', 9.5 * s, 14 * s, y0 + 12 * s, paint(MUTED, 1), FONT_LABEL)
  ui.setCursor(vec2(14 * s, y0 + 28 * s))
  nextScreen:control(vec2(W - 28 * s, 26 * s))

  drawLeft('TRACK-LIMIT CUT', 9.5 * s, 14 * s, y0 + 66 * s, paint(MUTED, 1), FONT_LABEL)
  ui.setCursor(vec2(14 * s, y0 + 82 * s))
  ui.setNextItemWidth(W - 28 * s)
  local tyres, moved = ui.slider('##tyres', prefs.tyresOut, 1, 4, 'Counts at %.0f wheels out', true)
  if moved then
    prefs.tyresOut = tyres
    steward.tyresOut = tyres
  end

  ui.setCursor(vec2(14 * s, y0 + 118 * s))
  if ui.checkbox('Open this window in career races', prefs.autoOpen) then prefs.autoOpen = not prefs.autoOpen end
end

-- How the car stands right now, including what it brought into this session.
function Screens.drawCar(W, H, s, car, y0)
  local left, right = 14 * s, W - 14 * s
  if context and context.DamageOff then
    drawLeft('Damage is off in this career.', 15 * s, left, y0 + 16 * s, paint(MUTED, 1), FONT_VALUE)
    return
  end

  local body = wear and wear.body or { car.damage[0], car.damage[1], car.damage[2], car.damage[3] }
  local engine = wear and wear.engine or car.engineLifeLeft
  local gearbox = wear and wear.gearbox or car.gearboxDamage
  local corners = wear and wear.suspension or { car.wheels[0].suspensionDamage, car.wheels[1].suspensionDamage, car.wheels[2].suspensionDamage, car.wheels[3].suspensionDamage }

  local function cell(label, amount, x, y, width)
    local level = math.max(0, math.min(1, amount))
    local color = level < 0.05 and GOOD or level < 0.25 and TEXT or level < 0.6 and WARN or BAD
    drawLeft(label, 9.5 * s, x, y, paint(MUTED, 1), FONT_LABEL)
    drawRight(level < 0.005 and 'OK' or string.format('%d%%', math.ceil(level * 100)), 13 * s, x + width, y - 2 * s, paint(color, 1), FONT_VALUE)
    ui.drawRectFilled(vec2(x, y + 15 * s), vec2(x + width, y + 18 * s), rgbm(1, 1, 1, 0.1))
    if level > 0 then ui.drawRectFilled(vec2(x, y + 15 * s), vec2(x + width * level, y + 18 * s), paint(color, 1)) end
  end

  local gap = 14 * s
  local half = (right - left - gap) / 2
  local y = y0 + 12 * s
  drawLeft('BODY', 10 * s, left, y, paint(TEXT, 1), FONT_LABEL)
  y = y + 18 * s
  cell('FRONT', body[1] / 100, left, y, half)
  cell('REAR', body[2] / 100, left + half + gap, y, half)
  y = y + 28 * s
  cell('LEFT', body[3] / 100, left, y, half)
  cell('RIGHT', body[4] / 100, left + half + gap, y, half)
  y = y + 34 * s
  cell('ENGINE', (1000 - engine) / 1000, left, y, half)
  cell('GEARBOX', gearbox, left + half + gap, y, half)
  y = y + 34 * s
  drawLeft('SUSPENSION', 10 * s, left, y, paint(TEXT, 1), FONT_LABEL)
  y = y + 18 * s
  cell('FRONT LEFT', corners[1], left, y, half)
  cell('FRONT RIGHT', corners[2], left + half + gap, y, half)
  y = y + 28 * s
  cell('REAR LEFT', corners[3], left, y, half)
  cell('REAR RIGHT', corners[4], left + half + gap, y, half)
  y = y + 32 * s
  hairline(left, right, y)

  if context and context.RepairRate and context.CarBody then
    local cost = (body[1] + body[2] + body[3] + body[4]) * 20 + math.max(0, 1000 - engine) * 8 + gearbox * 6000 + (corners[1] + corners[2] + corners[3] + corners[4]) * 3000
    local bill = math.ceil(cost * context.RepairRate / 50) * 50
    drawLeft('FULL REPAIR', 9.5 * s, left, y + 10 * s, paint(MUTED, 1), FONT_LABEL)
    drawRight(bill > 0 and ('ABOUT ' .. credits(bill) .. ' CR') or 'NOTHING TO FIX', 12 * s, right, y + 8 * s, paint(bill > 0 and WARN or GOOD, 1), FONT_LABEL)
  elseif context and not context.CarBody then
    drawLeft('Team car: repairs are on the team.', 13 * s, left, y + 8 * s, paint(MUTED, 1), FONT_VALUE)
  end
end

function Screens.drawMinimised(W, s, car, total)
  local height = 44 * s
  ui.drawRectFilled(vec2(0, 0), vec2(W, height), paint(INK, 0.9))
  ui.drawRectFilled(vec2(0, 0), vec2(W, 2 * s), paint(VOLT, 1))
  local position = 'P' .. car.racePosition
  drawLeft(position, 24 * s, 14 * s, 8 * s, paint(TEXT, 1), FONT_TITLE)
  drawLeft('/ ' .. total, 13 * s, 20 * s + measure(position, 24 * s, FONT_TITLE), 18 * s, paint(MUTED, 1), FONT_VALUE)
  local session = ac.getSession(ac.getSim().currentSessionIndex)
  local lapNow = car.lapCount + 1
  if session and session.laps > 0 then lapNow = math.min(lapNow, session.laps) end
  drawLeft('LAP ' .. lapNow .. (session and session.laps > 0 and (' / ' .. session.laps) or ''), 12 * s, 112 * s, 16 * s, paint(TEXT, 1), FONT_LABEL)

  local x, mid = 216 * s, height / 2
  ui.drawRectFilled(vec2(x - 7 * s, mid - 1 * s), vec2(x + 7 * s, mid + 1 * s), paint(VOLT, 1))
  ui.drawRectFilled(vec2(x - 1 * s, mid - 7 * s), vec2(x + 1 * s, mid + 7 * s), paint(VOLT, 1))
  ui.setCursor(vec2(0, 0))
  if ui.invisibleButton('##restore', vec2(W - 56 * s, height)) then prefs.minimised = false end
end

local function drawWindow(dt)
  local car = ac.getCar(0)
  if not car then return end

  timing.boardAge = timing.boardAge + dt
  if timing.boardAge >= 0.25 then
    timing.boardAge = 0
    refreshBoard()
  end

  local size = ui.windowSize()
  local W, H = size.x, size.y
  local s = math.max(0.8, math.min(1.8, W / 460))

  if prefs.minimised then
    Screens.drawMinimised(W, s, car, #timing.board)
    return
  end

  ui.drawRectFilled(vec2(0, 0), size, paint(INK, 0.9))
  if steward.flash > 0 then ui.drawRectFilled(vec2(0, 0), size, paint(BAD, 0.16 * steward.flash)) end

  drawHeader(W, s, car, #timing.board)
  local y0, worst = drawStatus(W, s, 68 * s, car, dt)
  ui.drawRectFilled(vec2(0, 0), vec2(W, 2 * s), paint(worst == 'ok' and VOLT or STATE_COLOR[worst], 1))

  if prefs.screen == 1 then
    drawLapsPage(W, H, s, car, y0)
  elseif prefs.screen == 2 then
    drawRacePage(W, H, s, y0)
  elseif prefs.screen == 4 then
    Screens.drawCar(W, H, s, car, y0)
  else
    drawSetupPage(W, s, y0)
  end
end

-- After the flag --------------------------------------------------------------------------------

local function drawClipped(text, size, a, b, color, font, wrap)
  ui.pushDWriteFont(font)
  ui.dwriteDrawTextClipped(text, size, a, b, ui.Alignment.Start, ui.Alignment.Start, wrap == true, color)
  ui.popDWriteFont()
end

local function signed(value)
  return value > 0 and '+' .. value or tostring(value)
end

local function leaveGame(action)
  if finish.leaving then return end
  closeGame(action)
  io.save(COMMAND_PATH, JSON.stringify({ RaceId = context.RaceId, Action = action }), true)
end

local buttonsLive = true
local hotButton = nil
local SOUND_FOLDER = ac.getFolder(ac.FolderID.Root) .. '/apps/lua/PaddockLadder/sounds/'

-- Each sound can only play once from a file event, so a fresh one replaces the last.
local function startUiSound(name)
  local d = finish.debrief
  if not d or not d.SoundsOn then return end
  if finish.sounds[name] then finish.sounds[name]:dispose() end
  local sound = ac.AudioEvent.fromFile({ filename = SOUND_FOLDER .. name .. '.wav', use3D = false, loop = false }, false)
  sound.volume = d.SoundsVolume / 100
  sound.cameraInteriorMultiplier = 1
  sound.cameraExteriorMultiplier = 1
  sound.cameraTrackMultiplier = 1
  sound:start()
  finish.sounds[name] = sound
  finish.soundCount = finish.soundCount + 1
end

-- A sound that fails must never get in the way of the buttons.
local function playUiSound(name)
  local ok, err = pcall(startUiSound, name)
  if not ok and not finish.soundFailed then
    finish.soundFailed = true
    ac.error('[PaddockLadder] sound stopped: ' .. tostring(err))
  end
end

-- This screen only receives raw mouse input, and a single click can be reported on more than one frame,
-- which would also press whatever the next page has in the same spot.
local function finishButton(label, a, b, kind, sound)
  local mouse = ui.mousePos()
  local hot = buttonsLive and mouse.x >= a.x and mouse.x <= b.x and mouse.y >= a.y and mouse.y <= b.y
  if hot then hotButton = label .. math.floor(a.x) .. ':' .. math.floor(a.y) end
  local height = b.y - a.y
  local size = height * (kind == 'link' and 0.36 or 0.3)
  local top = a.y + (height - size * 1.25) / 2
  if kind == 'primary' or kind == 'ghost' then
    if kind == 'primary' then ui.drawRectFilled(a, b, paint(VOLT, hot and 1 or 0.86))
    else ui.drawRect(a, b, paint(hot and VOLT or MUTED, 1)) end
    local width = measure(label, size, FONT_LABEL)
    drawLeft(label, size, a.x + (b.x - a.x - width) / 2, top, paint(kind == 'primary' and INK or (hot and VOLT or TEXT), 1), FONT_LABEL)
  elseif kind == 'tile' then
    if hot then ui.drawRect(a, b, paint(VOLT, 1)) end
  else
    drawRight(label, size, b.x, top, paint(hot and TEXT or MUTED, 1), FONT_LABEL)
  end

  if not hot or finish.clickWait > 0 or not finish.pressed then return false end
  finish.clickWait = CLICK_GAP_SECONDS
  playUiSound(sound or 'click')
  return true
end

-- Which rows to show when a list is longer than the space, keeping the player's row in view.
local function firstVisibleRow(rows, visible)
  local mine = 1
  for i, row in ipairs(rows) do
    if row.IsPlayer then mine = i end
  end
  return math.max(1, math.min(mine - math.floor(visible / 2), #rows - visible + 1))
end

local function drawFinishRow(row, x, y, width, height, s, cells, alpha)
  alpha = alpha or 1
  if row.IsPlayer then
    ui.drawRectFilled(vec2(x - 8 * s, y - 3 * s), vec2(x + width, y + height - 4 * s), rgbm(1, 1, 1, 0.08 * alpha))
    ui.drawRectFilled(vec2(x - 8 * s, y - 3 * s), vec2(x - 5 * s, y + height - 4 * s), paint(VOLT, alpha))
  end
  local nameColor = paint(row.IsPlayer and VOLT or row.Tag == 'TEAMMATE' and GOOD or row.Tag == 'RIVAL' and BAD or TEXT, alpha)
  local nameEnd = cells.teamX and x + cells.teamX - 10 * s or x + width - cells.nameGap
  drawLeft(tostring(row.Pos), 16 * s, x, y, paint(MUTED, alpha), FONT_VALUE)
  drawClipped(row.Name, 16 * s, vec2(x + 36 * s, y), vec2(nameEnd, y + height), nameColor, FONT_VALUE)
  if cells.teamX and row.Team and row.Team ~= '' then
    drawClipped(row.Team .. ((row.Tag and row.Tag ~= '') and ('  /  ' .. row.Tag) or ''), 13 * s, vec2(x + cells.teamX, y + 2 * s), vec2(x + width - cells.nameGap, y + height), paint(MUTED, alpha), FONT_VALUE)
  end
  drawRight(cells.right, 16 * s, x + width - 8 * s, y, paint(TEXT, alpha), FONT_VALUE)
  if cells.middle then drawRight(cells.middle, 16 * s, x + width - 62 * s, y, paint(MUTED, alpha), FONT_VALUE) end
end

local function appear(index, gap)
  return math.max(0, math.min(1, ((finish.stepClock or 0) - index * (gap or 0.08)) / 0.3))
end

local function drawFinishList(title, headRight, rows, x, y, width, height, s, cellsFor)
  drawLeft(title, 12 * s, x, y, paint(MUTED, 1), FONT_LABEL)
  drawRight(headRight, 12 * s, x + width - 8 * s, y, paint(MUTED, 1), FONT_LABEL)
  hairline(x, x + width, y + 20 * s)
  local rowHeight = 29 * s
  local top = y + 28 * s
  local visible = math.max(1, math.floor((height - 28 * s) / rowHeight))
  local first = firstVisibleRow(rows, visible)
  for i = first, math.min(#rows, first + visible - 1) do
    drawFinishRow(rows[i], x, top + (i - first) * rowHeight, width, rowHeight, s, cellsFor(rows[i]), appear(i - first))
  end
end

-- Shown until the launcher has scored the race, or if it isn't running.
local function drawAwaitingDebrief(W, H, s)
  local centre = W / 2
  local position = 'P' .. finish.position
  local width = measure(position, 150 * s, FONT_TITLE)
  drawLeft(position, 150 * s, centre - width / 2, H / 2 - 190 * s, paint(finish.position == 1 and VOLT or TEXT, 1), FONT_TITLE)
  local grid = 'OF ' .. finish.cars
  drawLeft(grid, 13 * s, centre - measure(grid, 13 * s, FONT_LABEL) / 2, H / 2 - 4 * s, paint(MUTED, 1), FONT_LABEL)

  local message
  if not finish.saved then message = 'Paddock Ladder could not save this result.'
  elseif finish.clock - FLAG_SECONDS < LAUNCHER_PATIENCE_SECONDS then message = 'Your team is reviewing the race.'
  else message = 'Result saved. Open Paddock Ladder to see how your team scored it.' end
  local size = 20 * s
  drawLeft(message, size, centre - measure(message, size, FONT_VALUE) / 2, H / 2 + 34 * s, paint(TEXT, 1), FONT_VALUE)

  if finish.clock - FLAG_SECONDS >= LAUNCHER_PATIENCE_SECONDS or not finish.saved then
    if finishButton('EXIT GAME', vec2(centre - 150 * s, H / 2 + 90 * s), vec2(centre + 150 * s, H / 2 + 162 * s), 'primary') then leaveGame('paddock') end
  end
end

local function drawFlagBanner(W, H, s)
  local bandTop, bandBottom = H / 2 - 110 * s, H / 2 + 110 * s
  ui.drawRectFilled(vec2(0, bandTop), vec2(W, bandBottom), paint(INK, 0.86))
  ui.drawRectFilled(vec2(0, bandTop), vec2(W, bandTop + 2 * s), paint(VOLT, 1))
  local label = 'CHEQUERED FLAG'
  drawLeft(label, 14 * s, W / 2 - measure(label, 14 * s, FONT_LABEL) / 2, bandTop + 24 * s, paint(MUTED, 1), FONT_LABEL)
  local position = 'P' .. finish.position
  drawLeft(position, 110 * s, W / 2 - measure(position, 110 * s, FONT_TITLE) / 2, bandTop + 44 * s, paint(TEXT, 1), FONT_TITLE)
end

-- Between seasons -------------------------------------------------------------------------------

sendCommand = function (action, fields, after)
  if finish.waiting then return end
  commandSeq = commandSeq + 1
  local command = fields or {}
  command.RaceId, command.Action, command.Seq = context.RaceId, action, commandSeq
  finish.waiting = { seq = commandSeq, clock = 0, poll = 0, after = after }
  io.save(COMMAND_PATH, JSON.stringify(command), true)
end

local function panelArea(W, H, s)
  local panelW, panelH = 1360 * s, 820 * s
  return (W - panelW) / 2, (H - panelH) / 2, panelW
end

local function drawPageHeading(eyebrow, title, hint, x, y, width, s, balance)
  ui.drawRectFilled(vec2(x, y - 14 * s), vec2(x + width, y - 12 * s), paint(VOLT, 1))
  drawLeft(eyebrow, 12 * s, x, y, paint(MUTED, 1), FONT_LABEL)
  drawLeft(title, 34 * s, x, y + 18 * s, paint(TEXT, 1), FONT_TITLE)
  local right = x + width
  if balance then
    local money = credits(balance) .. ' CR'
    drawRight(money, 24 * s, right, y + 30 * s, paint(balance < 0 and BAD or VOLT, 1), FONT_VALUE)
    right = right - measure(money, 24 * s, FONT_VALUE) - 32 * s
  end
  if hint and hint ~= '' then drawRight(hint, 14 * s, right, y + 36 * s, paint(MUTED, 1), FONT_VALUE) end
  hairline(x, x + width, y + 72 * s)
end

-- Lists longer than the screen turn over in pages, with the mouse wheel or these links.
local function drawPager(key, total, perPage, right, y, s)
  local pages = math.max(1, math.ceil(total / perPage))
  local page = math.min(finish.listPages[key] or 1, pages)
  if pages > 1 then
    local move = 0
    local wheel = buttonsLive and ui.mouseWheel() or 0
    if wheel ~= 0 and finish.clickWait <= 0 then
      finish.clickWait = CLICK_GAP_SECONDS
      move = wheel < 0 and 1 or -1
      playUiSound('scroll')
    end
    if finishButton('NEXT', vec2(right - 70 * s, y), vec2(right, y + 30 * s), 'link', 'scroll') then move = 1 end
    drawRight(string.format('%d / %d', page, pages), 13 * s, right - 86 * s, y + 7 * s, paint(TEXT, 1), FONT_VALUE)
    if finishButton('PREV', vec2(right - 220 * s, y), vec2(right - 150 * s, y + 30 * s), 'link', 'scroll') then move = -1 end
    page = math.max(1, math.min(pages, page + move))
  end
  finish.listPages[key] = page
  return (page - 1) * perPage + 1
end

local function drawPhoto(image, x, y, width, height)
  ui.drawRectFilled(vec2(x, y), vec2(x + width, y + height), rgbm(1, 1, 1, 0.05))
  if image and image ~= '' then ui.drawImage(image, vec2(x, y), vec2(x + width, y + height)) end
end

-- Results, one thing at a time -------------------------------------------------------------------

local function finishEyebrow(d)
  return 'SEASON ' .. d.SeasonNo
end

local STEP_NAMES = { result = 'RESULT', targets = 'TARGETS', stewards = 'STEWARDS', standings = 'STANDINGS', credits = 'CREDITS', car = 'CAR', contract = 'CONTRACT', season = 'SEASON', next = 'NEXT RACE' }

local function goToStep(page)
  finish.page, finish.stepClock, finish.revealed = page, 0, 0
end

local function resultSteps(d)
  local steps = { 'result', 'targets', 'standings', 'credits' }
  if #(d.Incidents or {}) > 0 then table.insert(steps, 3, 'stewards') end
  if finish.hadDamage then steps[#steps + 1] = 'car' end
  if d.ContractTitle ~= '' then steps[#steps + 1] = 'contract' end
  if d.SeasonOutcome ~= '' then steps[#steps + 1] = 'season' end
  if d.HasNextRace then steps[#steps + 1] = 'next' end
  return steps
end

local function drawStepFooter(d, x, y, width, s, primary)
  local footer, right = y + 748 * s, x + width
  hairline(x, right, y + 730 * s)

  local steps, current = resultSteps(d), 0
  for i, step in ipairs(steps) do
    if step == finish.page then current = i end
  end
  if current > 0 and not d.Signed then
    local left = x
    for i, step in ipairs(steps) do
      local label = STEP_NAMES[step]
      drawLeft(label, 12 * s, left, footer + 28 * s, paint(i == current and VOLT or i < current and TEXT or MUTED, i > current and 0.5 or 1), FONT_LABEL)
      if i == current then ui.drawRectFilled(vec2(left, footer + 48 * s), vec2(left + measure(label, 12 * s, FONT_LABEL), footer + 50 * s), paint(VOLT, 1)) end
      left = left + measure(label, 12 * s, FONT_LABEL) + 22 * s
    end
    if current > 1 and finishButton('BACK', vec2(right - 410 * s, footer + 18 * s), vec2(right - 320 * s, footer + 54 * s), 'link', 'scroll') then goToStep(steps[current - 1]) end
  end

  if primary then
    if finishButton(primary.label, vec2(right - 300 * s, footer), vec2(right, footer + 72 * s), 'primary', 'confirm') then primary.action() end
    return
  end
  local following = steps[current + 1]
  local label = (following and following ~= 'next') and 'CONTINUE' or d.ContinueLabel
  if finishButton(label, vec2(right - 300 * s, footer), vec2(right, footer + 72 * s), 'primary', 'confirm') then
    if following then goToStep(following)
    elseif d.Released then
      finish.marketReturn = finish.page
      goToStep('market')
    elseif d.CanChoose then goToStep((d.Ladder or #(d.Offers or {}) > 0) and 'offers' or 'series')
    else leaveGame('paddock') end
  end
end

local function drawResultStep(d, W, H, s)
  local x, y, width = panelArea(W, H, s)
  drawPageHeading('RACE RESULT  /  ' .. d.SeriesName:upper(), d.Title, nil, x, y, width, s, d.CreditsBalance)
  local top = y + 110 * s
  local position = 'P' .. d.Pos
  local alpha = appear(0)
  drawLeft(position, 190 * s, x - 8 * s, top - 34 * s, paint(d.Pos == 1 and VOLT or TEXT, alpha), FONT_TITLE)
  drawLeft('OF ' .. d.Grid, 14 * s, x + 4 * s, top + 216 * s, paint(MUTED, alpha), FONT_LABEL)

  local after = appear(3)
  drawLeft('POINTS', 12 * s, x + 4 * s, top + 270 * s, paint(MUTED, after), FONT_LABEL)
  drawLeft('+' .. d.Points, 52 * s, x + 4 * s, top + 288 * s, paint(TEXT, after), FONT_VALUE)
  local gained = d.StartPos - d.Pos
  drawLeft('STARTED', 12 * s, x + 190 * s, top + 270 * s, paint(MUTED, after), FONT_LABEL)
  drawLeft(d.StartPos > 0 and 'P' .. d.StartPos or '-', 52 * s, x + 190 * s, top + 288 * s, paint(TEXT, after), FONT_VALUE)
  if d.StartPos > 0 then
    drawLeft(gained == 0 and 'HELD POSITION' or (gained > 0 and 'GAINED ' .. gained or 'LOST ' .. -gained), 13 * s, x + 4 * s, top + 372 * s, paint(gained > 0 and GOOD or gained < 0 and BAD or MUTED, after), FONT_LABEL)
  end

  drawFinishList('CLASSIFICATION', 'BEST LAP', d.Classification or {}, x + 520 * s, top - 18 * s, width - 520 * s, 620 * s, s, function (row)
    return { right = lapText(row.BestMs), nameGap = 130 * s, teamX = 340 * s }
  end)
  drawStepFooter(d, x, y, width, s)
end

local function drawTargetRow(target, x, y, width, s, alpha)
  local color = target.Met and GOOD or BAD
  drawLeft(target.Met and 'MET' or 'MISSED', 12 * s, x, y + 7 * s, paint(color, alpha), FONT_LABEL)
  drawLeft(target.Session:upper(), 12 * s, x + 90 * s, y + 7 * s, paint(MUTED, alpha), FONT_LABEL)
  drawClipped(target.Text, 22 * s, vec2(x + 210 * s, y), vec2(x + width - 130 * s, y + 36 * s), paint(TEXT, alpha), FONT_VALUE)
  drawRight(target.Met and ('+' .. credits(target.Credits)) or '0', 20 * s, x + width - 8 * s, y + 2 * s, paint(target.Met and VOLT or MUTED, alpha), FONT_VALUE)
  hairline(x, x + width, y + 46 * s)
end

local function drawTargetsStep(d, W, H, s)
  local x, y, width = panelArea(W, H, s)
  drawPageHeading('WEEKEND TARGETS  /  ' .. d.Title:upper(), 'What the team asked for', nil, x, y, width, s, d.CreditsBalance)
  local listWidth = 880 * s
  local rowY = y + 96 * s
  local targets = d.Targets or {}
  for i, target in ipairs(targets) do
    drawTargetRow(target, x, rowY, listWidth, s, appear(i, 0.3))
    rowY = rowY + 56 * s
  end
  local noteAlpha = appear(#targets + 1, 0.3)
  for _, line in ipairs(d.Lines or {}) do
    if not line:match('^Met: ') and not line:match('^Missed: ') and rowY < y + 690 * s then
      drawClipped(line, 16 * s, vec2(x, rowY + 8 * s), vec2(x + listWidth, rowY + 36 * s), paint(MUTED, noteAlpha), FONT_VALUE)
      rowY = rowY + 32 * s
    end
  end

  local facts = {
    { 'TRACK LIMITS', tostring(d.Cuts), d.Cuts == 0 and GOOD or TEXT },
    { 'CONTACT', tostring(d.Contacts), d.Contacts == 0 and GOOD or TEXT },
    { 'TEAM TRUST', signed(d.TrustDelta), d.TrustDelta >= 0 and GOOD or BAD },
    { 'LICENCE POINTS', signed(d.LicenceDelta), d.LicenceDelta <= 0 and GOOD or BAD },
  }
  local factX = x + 960 * s
  for i, fact in ipairs(facts) do
    local alpha = appear(#targets + 1 + i, 0.3)
    local top = y + 96 * s + (i - 1) * 140 * s
    drawLeft(fact[1], 13 * s, factX, top, paint(MUTED, alpha), FONT_LABEL)
    drawLeft(fact[2], 64 * s, factX, top + 18 * s, paint(fact[3], alpha), FONT_VALUE)
  end
  drawStepFooter(d, x, y, width, s)
end

local function drawScorePanel(d, x, y, width, s, alpha)
  local color = d.ScoreGood and GOOD or d.ScoreDanger and BAD or TEXT
  drawLeft(d.ScoreTitle:upper(), 12 * s, x, y, paint(MUTED, alpha), FONT_LABEL)
  local total = tostring(d.ScoreTotal)
  drawLeft(total, 46 * s, x, y + 14 * s, paint(color, alpha), FONT_VALUE)
  drawLeft('/ 100', 16 * s, x + measure(total, 46 * s, FONT_VALUE) + 8 * s, y + 41 * s, paint(MUTED, alpha), FONT_VALUE)
  drawRight(d.ScoreVerdict, 15 * s, x + width - 8 * s, y + 39 * s, paint(TEXT, 0.85 * alpha), FONT_VALUE)
  local barY, barWidth = y + 74 * s, width - 8 * s
  ui.drawRectFilled(vec2(x, barY), vec2(x + barWidth, barY + 3 * s), rgbm(1, 1, 1, 0.12 * alpha))
  ui.drawRectFilled(vec2(x, barY), vec2(x + barWidth * math.min(1, d.ScoreTotal / 100) * alpha, barY + 3 * s), paint(color, alpha))
  local markX = x + barWidth * d.PromotionMark / 100
  ui.drawRectFilled(vec2(markX - 1 * s, barY - 5 * s), vec2(markX + 1 * s, barY + 8 * s), paint(TEXT, 0.9 * alpha))
  drawLeft(string.format('FINISH %d / 55     CLEAN %d / 30     TRUST %d / 15     PROMOTION AT %d', d.ScorePosition, d.ScoreClean, d.ScoreTrust, d.PromotionMark), 11 * s, x, barY + 14 * s, paint(MUTED, alpha), FONT_LABEL)
end

local function drawStandingsStep(d, W, H, s)
  local x, y, width = panelArea(W, H, s)
  drawPageHeading('CHAMPIONSHIP  /  ' .. d.SeriesName:upper(), d.SeasonOutcome ~= '' and 'Final standings' or 'Standings', nil, x, y, width, s, d.CreditsBalance)
  local top, listWidth = y + 92 * s, 760 * s
  drawFinishList('DRIVERS', 'WINS      PTS', d.Standings or {}, x + 8 * s, top, listWidth - 8 * s, 620 * s, s, function (row)
    return { right = tostring(row.Points), middle = tostring(row.Wins), nameGap = 130 * s, teamX = 300 * s }
  end)
  local rightX, rightW = x + listWidth + 48 * s, width - listWidth - 48 * s
  local tableHeight = d.ShowScore and 470 * s or 620 * s
  drawFinishList('TEAMS', 'WINS      PTS', d.TeamStandings or {}, rightX + 8 * s, top, rightW - 8 * s, tableHeight, s, function (row)
    return { right = tostring(row.Points), middle = tostring(row.Wins), nameGap = 130 * s }
  end)
  if d.ShowScore then drawScorePanel(d, rightX + 8 * s, y + 590 * s, rightW - 8 * s, s, appear(10)) end
  drawStepFooter(d, x, y, width, s)
end

local CREDIT_LINE_SECONDS = 0.35

local function drawCreditsStep(d, W, H, s)
  local x, y, width = panelArea(W, H, s)
  drawPageHeading('CREDITS  /  ' .. d.Title:upper(), 'What this race paid', nil, x, y, width, s, nil)
  local lines = d.CreditLines or {}
  local listX, listWidth = x + 180 * s, width - 360 * s
  local shown = math.min(#lines, math.floor(finish.stepClock / CREDIT_LINE_SECONDS))
  if shown > (finish.revealed or 0) then
    finish.revealed = shown
    playUiSound('scroll')
  end
  local rowY = y + 100 * s
  for i, line in ipairs(lines) do
    local alpha = math.max(0, math.min(1, (finish.stepClock - i * CREDIT_LINE_SECONDS) / 0.25))
    drawClipped(line.Label, 24 * s, vec2(listX, rowY), vec2(listX + listWidth - 240 * s, rowY + 40 * s), paint(TEXT, alpha), FONT_VALUE)
    drawRight((line.Amount > 0 and '+' or '') .. credits(line.Amount) .. ' CR', 24 * s, listX + listWidth, rowY, paint(line.Amount >= 0 and TEXT or BAD, alpha), FONT_VALUE)
    rowY = rowY + 52 * s
  end

  local start = (#lines + 1) * CREDIT_LINE_SECONDS
  local count = math.max(0, math.min(1, (finish.stepClock - start) / 0.8))
  hairline(listX, listX + listWidth, rowY + 4 * s)
  local net = d.CreditsNet * count
  drawLeft(d.CreditsNet < 0 and 'LOST' or 'EARNED', 16 * s, listX, rowY + 28 * s, paint(MUTED, 1), FONT_LABEL)
  drawRight((d.CreditsNet < 0 and '-' or '+') .. credits(math.abs(net)) .. ' CR', 44 * s, listX + listWidth, rowY + 14 * s, paint(d.CreditsNet < 0 and BAD or VOLT, 1), FONT_VALUE)
  drawLeft('BALANCE', 16 * s, listX, rowY + 96 * s, paint(MUTED, 1), FONT_LABEL)
  drawRight(credits(d.CreditsBalance - d.CreditsNet + net) .. ' CR', 30 * s, listX + listWidth, rowY + 88 * s, paint(d.CreditsBalance < 0 and BAD or TEXT, 1), FONT_VALUE)
  if d.DebtNote ~= '' then drawClipped(d.DebtNote, 16 * s, vec2(listX, rowY + 140 * s), vec2(listX + listWidth, rowY + 190 * s), paint(WARN, count), FONT_VALUE, true) end
  drawStepFooter(d, x, y, width, s)
end

local function drawSeasonStep(d, W, H, s)
  local x, y, width = panelArea(W, H, s)
  drawPageHeading('SEASON COMPLETE  /  ' .. d.SeriesName:upper(), 'Your season', nil, x, y, width, s, d.CreditsBalance)
  drawClipped(d.SeasonOutcome, 32 * s, vec2(x, y + 96 * s), vec2(x + width, y + 190 * s), paint(VOLT, appear(0)), FONT_VALUE, true)
  local cards = {
    { 'DRIVERS\' CHAMPION', d.ChampionName, d.ChampionTeam },
    { 'TEAMS\' CHAMPION', d.TeamChampion, '' },
    { 'YOU', 'P' .. d.FinalRank, d.TeamName },
    { 'YOUR TEAM', 'P' .. d.TeamRank, d.TeamName },
  }
  local cardWidth = width / 4
  for i, card in ipairs(cards) do
    local left, alpha = x + (i - 1) * cardWidth, appear(i, 0.3)
    drawLeft(card[1], 12 * s, left, y + 214 * s, paint(MUTED, alpha), FONT_LABEL)
    drawClipped(card[2], 30 * s, vec2(left, y + 234 * s), vec2(left + cardWidth - 22 * s, y + 280 * s), paint(TEXT, alpha), FONT_VALUE)
    drawClipped(card[3], 15 * s, vec2(left, y + 284 * s), vec2(left + cardWidth - 22 * s, y + 310 * s), paint(MUTED, alpha), FONT_VALUE)
  end
  if d.ShowScore then drawScorePanel(d, x, y + 360 * s, width * 0.62, s, appear(6, 0.3)) end
  drawStepFooter(d, x, y, width, s)
end

-- Everything about the next race, before committing to load it.
local function drawNextRaceStep(d, W, H, s)
  local x, y, width = panelArea(W, H, s)
  local b = d.Briefing
  if not b then
    goToStep('result')
    return
  end
  drawPageHeading(b.RoundText:upper() .. '  /  ' .. b.SeriesName:upper(), b.TrackName, nil, x, y, width, s, d.CreditsBalance)

  local top, photoW, photoH = y + 90 * s, 540 * s, 304 * s
  ui.drawRectFilled(vec2(x, top), vec2(x + photoW, top + photoH), rgbm(1, 1, 1, 0.05))
  if b.Image ~= '' then
    local a = appear(0)
    ui.drawImage(b.Image, vec2(x, top), vec2(x + photoW, top + photoH), rgbm(1, 1, 1, a))
  end
  local facts = { { 'LENGTH', b.LengthText }, { 'GRID', tostring(b.Grid) .. ' cars' }, { 'START', b.StartText }, { 'AIR', b.Ambient .. ' C' } }
  local factTop = top + photoH + 16 * s
  for i, fact in ipairs(facts) do
    local left = x + (i - 1) % 2 * photoW / 2
    local rowTop = factTop + math.floor((i - 1) / 2) * 56 * s
    drawLeft(fact[1], 11 * s, left, rowTop, paint(MUTED, 1), FONT_LABEL)
    drawClipped(fact[2], 21 * s, vec2(left, rowTop + 15 * s), vec2(left + photoW / 2 - 12 * s, rowTop + 46 * s), paint(TEXT, 1), FONT_VALUE)
  end
  drawLeft('FORECAST', 11 * s, x, factTop + 112 * s, paint(MUTED, 1), FONT_LABEL)
  drawClipped(b.Forecast, 17 * s, vec2(x, factTop + 127 * s), vec2(x + photoW, factTop + 154 * s), paint(TEXT, 1), FONT_VALUE)

  local rightX, rightW = x + 600 * s, width - 600 * s
  drawLeft('TARGETS AND WHAT THEY PAY', 12 * s, rightX, top, paint(MUTED, 1), FONT_LABEL)
  hairline(rightX, x + width, top + 20 * s)
  local rowY = top + 30 * s
  for i, target in ipairs(b.Targets or {}) do
    local alpha = appear(i, 0.12)
    drawLeft(target.Session:upper(), 11 * s, rightX, rowY + 7 * s, paint(MUTED, alpha), FONT_LABEL)
    drawClipped(target.Text, 19 * s, vec2(rightX + 110 * s, rowY), vec2(x + width - 130 * s, rowY + 30 * s), paint(TEXT, alpha), FONT_VALUE)
    drawRight('+' .. credits(target.Credits), 19 * s, x + width - 8 * s, rowY, paint(VOLT, alpha), FONT_VALUE)
    rowY = rowY + 36 * s
  end
  hairline(rightX, x + width, rowY + 2 * s)
  drawLeft(b.OnLoan and 'THE MOST YOU CAN KEEP' or 'THE MOST YOU CAN EARN', 13 * s, rightX, rowY + 20 * s, paint(MUTED, 1), FONT_LABEL)
  drawRight(credits(b.MaxCredits) .. ' CR', 32 * s, x + width - 8 * s, rowY + 10 * s, paint(VOLT, 1), FONT_VALUE)
  drawClipped(b.PrizeText, 14 * s, vec2(rightX, rowY + 58 * s), vec2(x + width, rowY + 80 * s), paint(MUTED, 1), FONT_VALUE)
  drawClipped(b.FinesText, 14 * s, vec2(rightX, rowY + 80 * s), vec2(x + width, rowY + 122 * s), paint(MUTED, 1), FONT_VALUE, true)

  local matchY = rowY + 130 * s
  for i, line in ipairs(b.Matchups or {}) do
    drawClipped(line, 15 * s, vec2(rightX, matchY + (i - 1) * 24 * s), vec2(x + width, matchY + i * 24 * s), paint(TEXT, 0.85 * appear(6 + i, 0.12)), FONT_VALUE)
  end

  -- The last chance to fix the car before the race loads.
  local conditionY = y + 664 * s
  if #(d.Market or {}) > 0 and not d.Signed and finishButton('CHANGE TEAM', vec2(x, conditionY), vec2(x + 240 * s, conditionY + 50 * s), 'ghost') then
    finish.marketReturn = 'next'
    goToStep('market')
  end
  if #(d.RivalChoices or {}) > 0 and finishButton(d.ChosenRival ~= '' and 'CHANGE RIVAL' or 'CHOOSE RIVAL', vec2(x + 260 * s, conditionY), vec2(x + 500 * s, conditionY + 50 * s), 'ghost') then
    goToStep('rival')
  end
  if d.RepairCost > 0 then
    drawClipped(d.DamageText .. ' will carry into this race.', 16 * s, vec2(rightX, conditionY + 14 * s), vec2(x + width - 290 * s, conditionY + 44 * s), paint(WARN, 1), FONT_VALUE)
    if finishButton('REPAIR CAR  ' .. credits(d.RepairCost), vec2(x + width - 270 * s, conditionY), vec2(x + width, conditionY + 50 * s), 'ghost') then
      finish.carReturn = 'next'
      goToStep('car')
    end
  else
    drawLeft('YOUR CAR IS READY', 13 * s, rightX, conditionY + 18 * s, paint(GOOD, 1), FONT_LABEL)
  end

  drawStepFooter(d, x, y, width, s, { label = 'LOAD RACE', action = function () leaveGame('next') end })
  if finishButton('PADDOCK LADDER', vec2(x + width - 640 * s, y + 766 * s), vec2(x + width - 430 * s, y + 802 * s), 'link') then leaveGame('paddock') end
end

local function drawCarDetailPage(d, W, H, s)
  local x, y, width = panelArea(W, H, s)
  local offer = finish.offer
  if not offer then
    goToStep('offers')
    return
  end
  drawPageHeading((offer.Requested and 'REQUESTED SEAT  /  ' or '') .. offer.Team:upper(), offer.CarName, nil, x, y, width, s, d.CreditsBalance)

  local photoW = 760 * s
  local photoH = photoW * 9 / 16
  drawPhoto(offer.Image, x, y + 90 * s, photoW, photoH)
  local specs = {
    { 'POWER', offer.Bhp > 0 and offer.Bhp .. ' bhp' or 'Not listed' }, { 'WEIGHT', offer.Kg > 0 and offer.Kg .. ' kg' or 'Not listed' },
    { 'POWER TO WEIGHT', offer.Pw > 0 and string.format('%.0f bhp/t', offer.Pw) or 'Not listed' },
    { 'TORQUE', offer.Torque ~= '' and offer.Torque or 'Not listed' }, { 'TOP SPEED', offer.TopSpeed ~= '' and offer.TopSpeed or 'Not listed' },
    { 'ACCELERATION', offer.Acceleration ~= '' and offer.Acceleration or 'Not listed' },
  }
  local specTop = y + 90 * s + photoH + 16 * s
  for i, spec in ipairs(specs) do
    local left = x + (i - 1) % 3 * photoW / 3
    local top = specTop + math.floor((i - 1) / 3) * 52 * s
    drawLeft(spec[1], 11 * s, left, top, paint(MUTED, 1), FONT_LABEL)
    drawClipped(spec[2], 20 * s, vec2(left, top + 15 * s), vec2(left + photoW / 3 - 10 * s, top + 44 * s), paint(TEXT, 1), FONT_VALUE)
  end

  local rightX, right = x + 810 * s, x + width
  drawLeft('SERIES', 11 * s, rightX, y + 90 * s, paint(MUTED, 1), FONT_LABEL)
  drawClipped(offer.SeriesName .. '  /  ' .. offer.TierName .. ' tier', 20 * s, vec2(rightX, y + 105 * s), vec2(right, y + 135 * s), paint(TEXT, 1), FONT_VALUE)

  drawLeft('THE TEAM WANTS', 11 * s, rightX, y + 150 * s, paint(MUTED, 1), FONT_LABEL)
  local rowY = y + 170 * s
  for _, clause in ipairs(offer.Clauses or {}) do
    drawClipped(clause, 19 * s, vec2(rightX, rowY), vec2(right, rowY + 28 * s), paint(TEXT, 1), FONT_VALUE)
    rowY = rowY + 30 * s
  end

  rowY = y + 300 * s
  drawLeft('THE DEAL', 11 * s, rightX, rowY, paint(MUTED, 1), FONT_LABEL)
  hairline(rightX, right, rowY + 18 * s)
  rowY = rowY + 28 * s
  local money = {}
  if offer.Owned then money[#money + 1] = { 'You already own this car', '0 CR', TEXT }
  else
    money[#money + 1] = { 'Car price', credits(offer.Price) .. ' CR', TEXT }
    if offer.TradeIn > 0 then money[#money + 1] = { 'Trade-in for your current car', '-' .. credits(offer.TradeIn) .. ' CR', GOOD } end
    if offer.SeatFee > 0 then money[#money + 1] = { 'Seat fee', credits(offer.SeatFee) .. ' CR', TEXT } end
    money[#money + 1] = offer.Loan and { 'Team loan: winnings halved', '0 CR', WARN } or { 'You pay', credits(offer.Pay) .. ' CR', TEXT }
  end
  if offer.Owned and offer.SeatFee > 0 then money[#money + 1] = { 'Seat fee', credits(offer.SeatFee) .. ' CR', TEXT } end
  if offer.Buyout > 0 then money[#money + 1] = { 'Buying out your contract', credits(offer.Buyout) .. ' CR', WARN } end
  money[#money + 1] = { 'Left after signing', credits(offer.CreditsAfter) .. ' CR', VOLT }
  for _, line in ipairs(money) do
    drawLeft(line[1], 20 * s, rightX, rowY, paint(MUTED, 1), FONT_VALUE)
    drawRight(line[2], 20 * s, right, rowY, paint(line[3], 1), FONT_VALUE)
    rowY = rowY + 36 * s
  end
  if offer.Description ~= '' then drawClipped(offer.Description, 14 * s, vec2(rightX, rowY + 14 * s), vec2(right, y + 716 * s), paint(MUTED, 1), FONT_VALUE, true) end

  local footer = y + 748 * s
  hairline(x, right, y + 730 * s)
  if finishButton(offer.MidSeason and 'BACK TO THE MARKET' or 'BACK TO OFFERS', vec2(right - 520 * s, footer + 18 * s), vec2(right - 320 * s, footer + 54 * s), 'link', 'scroll') then goToStep(offer.MidSeason and 'market' or 'offers') end
  if finishButton('SIGN CONTRACT', vec2(right - 300 * s, footer), vec2(right, footer + 72 * s), 'primary', 'confirm') then
    if offer.MidSeason then sendCommand('transfer')
    else sendCommand('sign', { OfferIndex = offer.Index }) end
  end
end

local function drawOfferCard(offer, x, y, width, s)
  local photoHeight = width * 9 / 16
  drawPhoto(offer.Image, x, y, width, photoHeight)
  local top = y + photoHeight + 14 * s
  drawClipped((offer.Requested and 'REQUESTED SEAT  /  ' or '') .. offer.Team:upper(), 12 * s, vec2(x, top), vec2(x + width, top + 18 * s), paint(VOLT, 1), FONT_LABEL)
  drawClipped(offer.CarName, 26 * s, vec2(x, top + 18 * s), vec2(x + width, top + 58 * s), paint(TEXT, 1), FONT_VALUE)

  drawLeft('SERIES', 11 * s, x, top + 64 * s, paint(MUTED, 1), FONT_LABEL)
  drawClipped(offer.SeriesName, 15 * s, vec2(x, top + 79 * s), vec2(x + width * 0.62, top + 101 * s), paint(TEXT, 1), FONT_VALUE)
  drawLeft('POWER TO WEIGHT', 11 * s, x + width * 0.65, top + 64 * s, paint(MUTED, 1), FONT_LABEL)
  drawLeft(offer.Pw > 0 and string.format('%.0f bhp/t', offer.Pw) or 'Not listed', 15 * s, x + width * 0.65, top + 79 * s, paint(TEXT, 1), FONT_VALUE)

  drawLeft('THE TEAM WANTS', 11 * s, x, top + 112 * s, paint(MUTED, 1), FONT_LABEL)
  for i, clause in ipairs(offer.Clauses or {}) do
    drawClipped(clause, 15 * s, vec2(x, top + (106 + i * 24) * s), vec2(x + width, top + (130 + i * 24) * s), paint(TEXT, 1), FONT_VALUE)
  end

  drawLeft('YOUR COST', 11 * s, x, top + 216 * s, paint(MUTED, 1), FONT_LABEL)
  drawClipped(offer.Cost, 20 * s, vec2(x, top + 231 * s), vec2(x + width * 0.62, top + 262 * s), paint(offer.Loan and WARN or VOLT, 1), FONT_VALUE)
  drawLeft('LEFT AFTER SIGNING', 11 * s, x + width * 0.65, top + 216 * s, paint(MUTED, 1), FONT_LABEL)
  drawLeft(credits(offer.CreditsAfter) .. ' CR', 20 * s, x + width * 0.65, top + 231 * s, paint(TEXT, 1), FONT_VALUE)
end

local function drawOffersPage(d, W, H, s)
  local x, y, width = panelArea(W, H, s)
  drawPageHeading(finishEyebrow(d), d.OffersTitle, d.OffersHint, x, y, width, s, d.CreditsBalance)

  local cardWidth = 423 * s
  local offers = d.Offers or {}
  local first = drawPager('offers', #offers, 3, x + width, y + 6 * s, s)
  for i = first, math.min(#offers, first + 2) do
    local left = x + (i - first) * (cardWidth + 45 * s)
    drawOfferCard(offers[i], left, y + 92 * s, cardWidth, s)
    if finishButton('VIEW CAR', vec2(left, y + 650 * s), vec2(left + 200 * s, y + 706 * s), 'primary') then
      finish.offer = offers[i]
      goToStep('cardetail')
    end
  end

  local footer = y + 748 * s
  hairline(x, x + width, y + 730 * s)
  local right = x + width
  if d.Ladder then
    drawLeft('WANT A DIFFERENT SEAT?', 12 * s, x, footer, paint(MUTED, 1), FONT_LABEL)
    local note = d.SeatNote ~= '' and d.SeatNote or 'Your manager can approach teams in any series. Each team that turns you down makes the next one harder.'
    drawClipped(note, 15 * s, vec2(x, footer + 20 * s), vec2(right - 720 * s, footer + 72 * s), paint(d.SeatNote ~= '' and VOLT or MUTED, 1), FONT_VALUE, true)
    if finishButton('REQUEST A SEAT', vec2(right - 260 * s, footer + 8 * s), vec2(right, footer + 64 * s), 'ghost') then finish.page = 'seats' end
  else
    if finishButton('BACK TO ALL SERIES', vec2(right - 260 * s, footer + 8 * s), vec2(right, footer + 64 * s), 'ghost') then
      sendCommand('series', nil, function () finish.page = 'series' end)
    end
  end
  if finishButton('PADDOCK LADDER', vec2(right - 470 * s, footer + 18 * s), vec2(right - 290 * s, footer + 54 * s), 'link') then leaveGame('paddock') end
  if finishButton('RESULTS', vec2(right - 640 * s, footer + 18 * s), vec2(right - 500 * s, footer + 54 * s), 'link') then goToStep('result') end
end

local function drawSeatRow(seat, x, y, width, s)
  drawClipped(seat.Name, 18 * s, vec2(x, y), vec2(x + width - 300 * s, y + 28 * s), paint(seat.Allowed and TEXT or MUTED, 1), FONT_VALUE)
  drawClipped(string.format('%s  /  %s tier.  %s', seat.Group, seat.TierName, seat.Reason), 13 * s, vec2(x, y + 28 * s), vec2(x + width - 300 * s, y + 50 * s), paint(MUTED, 1), FONT_VALUE)
  drawLeft('ODDS', 11 * s, x + width - 270 * s, y + 4 * s, paint(MUTED, 1), FONT_LABEL)
  drawLeft(seat.Allowed and seat.Chance .. '%' or seat.Asked and 'Asked' or 'Closed', 18 * s, x + width - 270 * s, y + 19 * s, paint(seat.Allowed and TEXT or MUTED, 1), FONT_VALUE)
  if seat.Allowed and seat.Market and finishButton('APPROACH', vec2(x + width - 130 * s, y + 8 * s), vec2(x + width, y + 50 * s), 'ghost') then
    finish.modal = {
      title = 'Approach ' .. seat.Name .. '?',
      text = string.format("Your odds are %d%%. Nothing is paid unless you sign, but this team won't be asked twice this season.", seat.Chance),
      yes = 'APPROACH',
      onYes = function ()
        sendCommand('approach', { SeriesId = seat.SeriesId, Team = seat.Team }, function (data)
          if data.MarketOffer then
            finish.offer = data.MarketOffer
            goToStep('cardetail')
          end
        end)
      end,
    }
  elseif seat.Allowed and not seat.Market and finishButton('REQUEST', vec2(x + width - 130 * s, y + 8 * s), vec2(x + width, y + 50 * s), 'ghost') then
    finish.modal = {
      title = 'Request a seat in ' .. seat.Name .. '?',
      text = string.format("Your odds are %d%%. Your current offers stay whatever the answer, but this team won't be asked twice, and a no makes other moves up harder.", seat.Chance),
      yes = 'MAKE REQUEST',
      onYes = function () sendCommand('request', { SeriesId = seat.SeriesId }, function () finish.page = 'offers' end) end,
    }
  end
  hairline(x, x + width, y + 58 * s)
end

local function drawSeatsPage(d, W, H, s)
  local x, y, width = panelArea(W, H, s)
  drawPageHeading(finishEyebrow(d), 'Request a seat', nil, x, y, width, s, d.CreditsBalance)
  local facts = { { 'YOUR TIER', d.TierName }, { 'DRIVER RATING', tostring(d.DriverRating) }, { 'LICENCE', d.Licence }, { 'TITLES', tostring(d.Titles) } }
  for i, fact in ipairs(facts) do
    local left = x + (i - 1) * 190 * s
    drawLeft(fact[1], 11 * s, left, y + 88 * s, paint(MUTED, 1), FONT_LABEL)
    drawLeft(fact[2], 22 * s, left, y + 103 * s, paint(TEXT, 1), FONT_VALUE)
  end
  hairline(x, x + width, y + 146 * s)

  local seats = d.Seats or {}
  local perPage = 8
  local first = drawPager('seats', #seats, perPage, x + width, y + 6 * s, s)
  for i = first, math.min(#seats, first + perPage - 1) do
    drawSeatRow(seats[i], x, y + (156 + (i - first) * 62) * s, width, s)
  end

  local footer = y + 748 * s
  hairline(x, x + width, y + 730 * s)
  drawClipped('One tier up needs a driver rating of 70. Two tiers up needs 80 and last season\'s title. Each series can be asked once an off-season.', 15 * s, vec2(x, footer + 16 * s), vec2(x + width - 300 * s, footer + 72 * s), paint(MUTED, 1), FONT_VALUE, true)
  if finishButton('BACK TO OFFERS', vec2(x + width - 260 * s, footer + 8 * s), vec2(x + width, footer + 64 * s), 'ghost') then finish.page = 'offers' end
end

local function drawSeriesPage(d, W, H, s)
  local x, y, width = panelArea(W, H, s)
  drawPageHeading(finishEyebrow(d), 'Pick a series', nil, x, y, width, s, d.CreditsBalance)
  local choices = d.SeriesChoices or {}
  local perPage = 8
  local first = drawPager('series', #choices, perPage, x + width, y + 6 * s, s)
  local tileWidth, tileHeight = 322 * s, 260 * s
  for i = first, math.min(#choices, first + perPage - 1) do
    local slot = i - first
    local left = x + slot % 4 * (tileWidth + 24.5 * s)
    local top = y + 92 * s + math.floor(slot / 4) * (tileHeight + 22 * s)
    local choice = choices[i]
    drawPhoto(choice.Image, left, top, tileWidth, tileWidth * 9 / 16)
    drawLeft(choice.Group:upper(), 11 * s, left, top + 190 * s, paint(MUTED, 1), FONT_LABEL)
    drawClipped(choice.Name, 19 * s, vec2(left, top + 205 * s), vec2(left + tileWidth, top + 233 * s), paint(TEXT, 1), FONT_VALUE)
    drawLeft(choice.Detail:upper(), 11 * s, left, top + 235 * s, paint(MUTED, 1), FONT_LABEL)
    if finishButton('', vec2(left, top), vec2(left + tileWidth, top + tileHeight), 'tile') then
      sendCommand('pick', { SeriesId = choice.SeriesId }, function () finish.page = 'offers' end)
    end
  end

  local footer = y + 748 * s
  hairline(x, x + width, y + 730 * s)
  if finishButton('PADDOCK LADDER', vec2(x + width - 180 * s, footer + 18 * s), vec2(x + width, footer + 54 * s), 'link') then leaveGame('paddock') end
  if finishButton('RESULTS', vec2(x + width - 340 * s, footer + 18 * s), vec2(x + width - 200 * s, footer + 54 * s), 'link') then goToStep('result') end
end

-- Every contact gets a ruling, so being hit is never treated like doing the hitting.
function Screens.drawStewards(d, W, H, s)
  local x, y, width = panelArea(W, H, s)
  drawPageHeading('STEWARDS\' ROOM  /  ' .. d.Title:upper(), 'Every contact, ruled on', nil, x, y, width, s, d.CreditsBalance)
  for i, incident in ipairs(d.Incidents or {}) do
    if i > 8 then break end
    local top, alpha = y + (96 + (i - 1) * 74) * s, appear(i, 0.3)
    local color = incident.Who == 'you' and BAD or incident.Who == 'them' and GOOD or MUTED
    drawLeft('LAP ' .. incident.Lap, 13 * s, x, top + 9 * s, paint(MUTED, alpha), FONT_LABEL)
    drawClipped(incident.Text, 26 * s, vec2(x + 100 * s, top), vec2(x + width * 0.62, top + 44 * s), paint(TEXT, alpha), FONT_VALUE)
    drawRight(incident.Verdict, 20 * s, x + width - 8 * s, top + 6 * s, paint(color, alpha), FONT_VALUE)
    hairline(x, x + width, top + 58 * s)
  end
  drawStepFooter(d, x, y, width, s)
end

function Screens.drawContract(d, W, H, s)
  local x, y, width = panelArea(W, H, s)
  drawPageHeading('YOUR CONTRACT', d.ContractTitle, nil, x, y, width, s, d.CreditsBalance)
  drawClipped(d.ContractText, 26 * s, vec2(x, y + 110 * s), vec2(x + width * 0.75, y + 240 * s), paint(TEXT, appear(0)), FONT_VALUE, true)
  drawLeft('TEAM TRUST', 13 * s, x, y + 270 * s, paint(MUTED, 1), FONT_LABEL)
  drawLeft(tostring(d.TeamTrust), 64 * s, x, y + 290 * s, paint(d.TeamTrust >= 40 and TEXT or BAD, appear(3)), FONT_VALUE)
  drawStepFooter(d, x, y, width, s)
end

function Screens.drawRivalPick(d, W, H, s)
  local x, y, width = panelArea(W, H, s)
  drawPageHeading('YOUR RIVAL', 'Choose a rival', 'They fight harder near you. Beating them pays extra.', x, y, width, s, d.CreditsBalance)
  local rows = d.RivalChoices or {}
  local perPage = 10
  local first = drawPager('rivals', #rows, perPage, x + width, y + 6 * s, s)
  for i = first, math.min(#rows, first + perPage - 1) do
    local row = rows[i]
    local top = y + (92 + (i - first) * 62) * s
    local chosen = row.Name == d.ChosenRival
    drawLeft(tostring(row.Pos), 18 * s, x, top + 8 * s, paint(MUTED, 1), FONT_VALUE)
    drawClipped(row.Name, 22 * s, vec2(x + 56 * s, top + 4 * s), vec2(x + 520 * s, top + 40 * s), paint(chosen and BAD or TEXT, 1), FONT_VALUE)
    drawClipped(row.Team, 16 * s, vec2(x + 540 * s, top + 8 * s), vec2(x + 960 * s, top + 36 * s), paint(MUTED, 1), FONT_VALUE)
    drawRight(row.Points .. ' PTS', 16 * s, x + width - 170 * s, top + 8 * s, paint(TEXT, 1), FONT_VALUE)
    if chosen then drawRight('YOUR RIVAL', 12 * s, x + width - 8 * s, top + 12 * s, paint(BAD, 1), FONT_LABEL)
    elseif finishButton('CHOOSE', vec2(x + width - 130 * s, top + 2 * s), vec2(x + width, top + 44 * s), 'ghost') then
      sendCommand('rival', { Team = row.Name }, function () goToStep('next') end)
    end
    hairline(x, x + width, top + 54 * s)
  end

  local footer, right = y + 748 * s, x + width
  hairline(x, right, y + 730 * s)
  if d.ChosenRival ~= '' and finishButton('NO RIVAL', vec2(x, footer + 8 * s), vec2(x + 240 * s, footer + 64 * s), 'ghost') then
    sendCommand('rival', { Team = '' }, function () goToStep('next') end)
  end
  if finishButton('BACK', vec2(right - 300 * s, footer + 8 * s), vec2(right, footer + 64 * s), 'ghost', 'scroll') then goToStep('next') end
end

function Screens.drawMarket(d, W, H, s)
  local x, y, width = panelArea(W, H, s)
  drawPageHeading(d.Released and 'YOUR CONTRACT WAS CANCELLED' or 'TRANSFER MARKET', d.Released and 'Find a new team' or 'Look for another team', nil, x, y, width, s, d.CreditsBalance)
  local seats = d.Market or {}
  local perPage = 8
  local first = drawPager('market', #seats, perPage, x + width, y + 6 * s, s)
  for i = first, math.min(#seats, first + perPage - 1) do
    drawSeatRow(seats[i], x, y + (92 + (i - first) * 62) * s, width, s)
  end
  if #seats == 0 then drawLeft('No teams are looking for a driver.', 22 * s, x, y + 100 * s, paint(MUTED, 1), FONT_VALUE) end

  local footer, right = y + 748 * s, x + width
  hairline(x, right, y + 730 * s)
  local note = d.Released and 'You owe your old team nothing. Staying in this series keeps your points. A new series starts a new table.'
    or d.CanLeave and ('Leaving costs ' .. credits(d.Buyout) .. ' CR if you sign. Staying in this series keeps your points. A new series starts a new table.')
    or ('You can\'t afford the ' .. credits(d.Buyout) .. ' CR to buy out your contract.')
  drawClipped(note, 15 * s, vec2(x, footer + 16 * s), vec2(right - 320 * s, footer + 72 * s), paint(d.CanLeave and MUTED or BAD, 1), FONT_VALUE, true)
  if d.Released then
    if finishButton('SIT OUT THE SEASON', vec2(right - 300 * s, footer + 8 * s), vec2(right, footer + 64 * s), 'ghost') then
      finish.modal = {
        title = 'Sit out the rest of the season?',
        text = 'You\'ll score nothing in the remaining rounds, and look for a new drive next season.',
        yes = 'SIT OUT',
        onYes = function () sendCommand('sitout', nil, function () goToStep('offers') end) end,
      }
    end
  elseif finishButton('BACK', vec2(right - 300 * s, footer + 8 * s), vec2(right, footer + 64 * s), 'ghost', 'scroll') then
    goToStep(finish.marketReturn or 'next')
  end
end

local function overviewPage()
  return finish.kind == 'session' and 'garage' or 'car'
end

-- The car's condition at a glance. Each row opens that part on its own screen.
local function drawGarageRow(part, d, x, y, width, s, alpha)
  local textX = x + 150 * s -- the game's own Assists box can cover the far left at the start of a session
  drawLeft(part.Name, 26 * s, textX, y, paint(TEXT, alpha), FONT_VALUE)
  drawLeft(part.Severity:upper(), 12 * s, textX, y + 39 * s, paint(WARN, alpha), FONT_LABEL)
  drawClipped(part.Effect, 15 * s, vec2(textX + 110 * s, y + 36 * s), vec2(x + width - 400 * s, y + 62 * s), paint(MUTED, alpha), FONT_VALUE)
  drawLeft('FULL REPAIR', 11 * s, x + width - 330 * s, y + 8 * s, paint(MUTED, alpha), FONT_LABEL)
  drawLeft(credits(part.Cost) .. ' CR', 22 * s, x + width - 330 * s, y + 25 * s, paint(part.Cost <= d.CreditsBalance and TEXT or BAD, alpha), FONT_VALUE)
  hairline(x, x + width, y + 74 * s)
  if finishButton('OPTIONS', vec2(x + width - 170 * s, y + 8 * s), vec2(x + width, y + 58 * s), 'ghost') then
    finish.part = part.Id
    goToStep('part')
  end
end

local function drawGaragePage(d, W, H, s)
  local x, y, width = panelArea(W, H, s)
  local review = finish.kind == 'session'
  drawPageHeading(review and finish.title or 'YOUR CAR', 'Car condition', 'Pick a part to see what it needs.', x, y, width, s, d and d.CreditsBalance)

  local footer, right = y + 748 * s, x + width
  if d then
    local parts = d.Parts or {}
    for i, part in ipairs(parts) do drawGarageRow(part, d, x, y + (92 + (i - 1) * 88) * s, width, s, appear(i, 0.12)) end
    if #parts == 0 then drawLeft('Your car is in perfect condition.', 26 * s, x + 150 * s, y + 100 * s, paint(TEXT, 1), FONT_VALUE) end
    if #parts > 1 and d.CanAffordRepair and finishButton('REPAIR EVERYTHING  ' .. credits(d.RepairCost), vec2(x + 150 * s, y + 462 * s), vec2(x + 550 * s, y + 518 * s), 'ghost') then sendCommand('repair') end
    if #parts > 0 then drawClipped('Whatever you leave stays on the car.', 15 * s, vec2(x + 150 * s, y + 534 * s), vec2(right, y + 560 * s), paint(MUTED, 1), FONT_VALUE) end
  else
    local message = finish.noReply and 'Paddock Ladder isn\'t answering, so repairs aren\'t available. The damage stays on the car.' or 'Checking the car.'
    drawLeft(message, 22 * s, x + 150 * s, y + 100 * s, paint(TEXT, 1), FONT_VALUE)
  end

  if review then
    hairline(x, right, y + 730 * s)
    if finishButton(finish.nextLabel, vec2(right - 380 * s, footer), vec2(right, footer + 72 * s), 'primary', 'confirm') then finish.closeReview = true end
  elseif finish.carReturn then
    drawStepFooter(d, x, y, width, s, { label = 'BACK TO RACE DETAILS', action = function ()
      local back = finish.carReturn
      finish.carReturn = nil
      goToStep(back)
    end })
  else
    drawStepFooter(d, x, y, width, s)
  end
end

-- One part, what leaving it means, and what can be done about it.
local function drawPartPage(d, W, H, s)
  local x, y, width = panelArea(W, H, s)
  local part
  for _, candidate in ipairs(d and d.Parts or {}) do
    if candidate.Id == finish.part then part = candidate end
  end
  if not part then
    goToStep(overviewPage())
    return
  end
  drawPageHeading(finish.kind == 'session' and finish.title or 'YOUR CAR', part.Name, nil, x, y, width, s, d.CreditsBalance)

  drawLeft('CONDITION', 12 * s, x, y + 100 * s, paint(MUTED, 1), FONT_LABEL)
  drawLeft(part.Severity, 52 * s, x, y + 118 * s, paint(WARN, appear(0)), FONT_VALUE)
  drawLeft('ON TRACK', 12 * s, x, y + 210 * s, paint(MUTED, 1), FONT_LABEL)
  drawClipped(part.Effect, 24 * s, vec2(x, y + 230 * s), vec2(x + 680 * s, y + 276 * s), paint(TEXT, appear(2)), FONT_VALUE, true)
  drawLeft('IF YOU LEAVE IT', 12 * s, x, y + 300 * s, paint(MUTED, 1), FONT_LABEL)
  drawClipped(part.IfLeft, 22 * s, vec2(x, y + 320 * s), vec2(x + 680 * s, y + 420 * s), paint(TEXT, appear(4)), FONT_VALUE, true)

  local left, right = x + 760 * s, x + width
  local back = function () goToStep(overviewPage()) end
  drawLeft('YOUR OPTIONS', 12 * s, left, y + 100 * s, paint(MUTED, 1), FONT_LABEL)
  if part.Cost <= d.CreditsBalance then
    if finishButton('REPAIR  ' .. credits(part.Cost) .. ' CR', vec2(left, y + 290 * s), vec2(right, y + 362 * s), 'primary', 'confirm') then sendCommand('repair', { Part = part.Id }, back) end
    drawClipped('Back to perfect. ' .. credits(d.CreditsBalance - part.Cost) .. ' CR left afterwards.', 15 * s, vec2(left, y + 258 * s), vec2(right, y + 284 * s), paint(MUTED, 1), FONT_VALUE)
  else
    drawClipped('You can\'t afford the full ' .. credits(part.Cost) .. ' CR repair.', 20 * s, vec2(left, y + 300 * s), vec2(right, y + 340 * s), paint(BAD, 1), FONT_VALUE)
  end
  if part.HalfCost > 0 and part.HalfCost <= d.CreditsBalance then
    if finishButton('REPAIR HALF  ' .. credits(part.HalfCost) .. ' CR', vec2(left, y + 382 * s), vec2(right, y + 446 * s), 'ghost') then sendCommand('repair', { Part = part.Id, Half = true }, back) end
  end
  if finishButton('LEAVE IT', vec2(left, y + 466 * s), vec2(right, y + 530 * s), 'ghost', 'scroll') then back() end
end

local function drawModal(W, H, s)
  local modal = finish.modal
  buttonsLive = true
  ui.drawRectFilled(vec2(0, 0), vec2(W, H), paint(INK, 0.7))
  local a, b = vec2(W / 2 - 360 * s, H / 2 - 150 * s), vec2(W / 2 + 360 * s, H / 2 + 150 * s)
  ui.drawRectFilled(a, b, paint(INK, 1))
  ui.drawRectFilled(a, vec2(b.x, a.y + 2 * s), paint(VOLT, 1))
  drawClipped(modal.title, 28 * s, vec2(a.x + 32 * s, a.y + 28 * s), vec2(b.x - 32 * s, a.y + 72 * s), paint(TEXT, 1), FONT_VALUE)
  drawClipped(modal.text, 17 * s, vec2(a.x + 32 * s, a.y + 84 * s), vec2(b.x - 32 * s, b.y - 100 * s), paint(MUTED, 1), FONT_VALUE, true)

  local yesA, yesB = vec2(b.x - 252 * s, b.y - 92 * s), vec2(b.x - 32 * s, b.y - 32 * s)
  if finishButton(modal.yes or 'OK', yesA, yesB, 'primary', modal.onYes and 'confirm' or nil) then
    finish.modal = nil
    if modal.onYes then modal.onYes() end
  elseif modal.onYes and finishButton('CANCEL', vec2(b.x - 420 * s, b.y - 80 * s), vec2(b.x - 280 * s, b.y - 44 * s), 'link') then
    finish.modal = nil
  end
end

local function drawFinishScreen()
  local size = ui.windowSize()
  local W, H = size.x, size.y
  local s = math.max(0.55, math.min(W / 1920, H / 1080))
  ui.setMouseCursor(ui.MouseCursor.Arrow)
  ui.captureMouse(true) -- the game's own menu is still underneath and would otherwise take these clicks too

  -- At the start of a session that hidden menu can swallow the click before this screen hears about it,
  -- so the button is read directly.
  local mouseDown = ac.isKeyDown(ui.KeyIndex.LeftButton)
  finish.pressed = mouseDown and not finish.mouseWasDown
  finish.mouseWasDown = mouseDown

  if finish.clock < FLAG_SECONDS then
    drawFlagBanner(W, H, s)
    return
  end
  ui.drawRectFilled(vec2(0, 0), size, paint(INK, 0.88 * math.min(1, (finish.clock - FLAG_SECONDS) / 0.4)))
  buttonsLive = not (finish.leaving or finish.modal or finish.waiting)
  hotButton = nil

  local d = finish.debrief
  if d and finish.hadDamage == nil then
    finish.hadDamage = d.RepairCost > 0
    finish.stepClock = 0
  end
  if finish.page == 'garage' or (finish.page == 'car' and d) then drawGaragePage(d, W, H, s)
  elseif not d then drawAwaitingDebrief(W, H, s)
  elseif finish.page == 'part' then drawPartPage(d, W, H, s)
  elseif finish.page == 'offers' then drawOffersPage(d, W, H, s)
  elseif finish.page == 'cardetail' then drawCarDetailPage(d, W, H, s)
  elseif finish.page == 'seats' then drawSeatsPage(d, W, H, s)
  elseif finish.page == 'series' then drawSeriesPage(d, W, H, s)
  elseif finish.page == 'targets' then drawTargetsStep(d, W, H, s)
  elseif finish.page == 'standings' then drawStandingsStep(d, W, H, s)
  elseif finish.page == 'credits' then drawCreditsStep(d, W, H, s)
  elseif finish.page == 'season' then drawSeasonStep(d, W, H, s)
  elseif finish.page == 'stewards' then Screens.drawStewards(d, W, H, s)
  elseif finish.page == 'contract' then Screens.drawContract(d, W, H, s)
  elseif finish.page == 'market' then Screens.drawMarket(d, W, H, s)
  elseif finish.page == 'rival' then Screens.drawRivalPick(d, W, H, s)
  elseif finish.page == 'next' then drawNextRaceStep(d, W, H, s)
  else drawResultStep(d, W, H, s) end

  if finish.modal then
    hotButton = nil -- only the pop-up's own buttons can be pressed
    drawModal(W, H, s)
  end
  if hotButton ~= finish.lastHotButton then
    if hotButton and finish.clickWait <= 0 then playUiSound('hover') end
    finish.lastHotButton = hotButton
  end
  if finish.leaving then
    ui.drawRectFilled(vec2(0, 0), size, paint(INK, 0.75))
    local label = finish.leaving == 'next' and 'LOADING NEXT RACE' or 'CLOSING THE GAME'
    drawLeft(label, 20 * s, W / 2 - measure(label, 20 * s, FONT_LABEL) / 2, H / 2 - 12 * s, paint(VOLT, 1), FONT_LABEL)
  elseif finish.waiting then
    drawRight('ONE MOMENT', 12 * s, W - 40 * s, 40 * s, paint(VOLT, 1), FONT_LABEL)
  end
end

-- If this screen ever fails, the game's own results come back rather than leaving the player stuck.
local finishScreenFailed = false

finishHUD = function ()
  if finishScreenFailed or not finish or finish.closeReview then return false end
  local ok, err = pcall(drawFinishScreen)
  if not ok then
    finishScreenFailed = true
    ac.error('[PaddockLadder] results screen stopped: ' .. tostring(err))
    return false
  end
  return true
end

-- A problem in the display must never stop the race from being scored.
local displayFailed = false

function script.windowMain(dt)
  if displayFailed then return end
  local ok, err = pcall(drawWindow, dt)
  if not ok then
    displayFailed = true
    ac.error('[PaddockLadder] window stopped: ' .. tostring(err))
  end
end
