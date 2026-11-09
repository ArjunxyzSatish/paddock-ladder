-- Runs inside the real game during run-game-tests.ps1. It steps through every session, records what the
-- game reports, and checks the track-limits steward. The script reads the report and removes this app.

local Steward = require('src/steward')

local DATA_DIR = ac.getFolder(ac.FolderID.ACDocuments) .. '/paddock_ladder'
local REPORT_PATH = DATA_DIR .. '/game_test_report.json'
local MAIN_WINDOW = 'IMGUI_LUA_Paddock Ladder_main'
local SETTLE_SECONDS = 6
local SKIP_RETRY_SECONDS = 3

local report = { sessionsCount = 0, sessions = {}, stewardFailures = {}, stewardChecks = 0, finished = false }
local currentIndex, sessionClock, sampled, skipClock, startClock = nil, 0, false, 0, 0

local function sample(input)
  return { wheelsOutside = input[1], speedKmh = input[2], inPitlane = input[3] or false, spline = input[4] or 0.25, lapCount = input[5] or 0 }
end

local function check(name, ok)
  report.stewardChecks = report.stewardChecks + 1
  if not ok then report.stewardFailures[#report.stewardFailures + 1] = name end
end

local function testSteward()
  local s = Steward.new(3)
  s:update(0.1, sample({ 4, 100 }))
  check('a brief excursion is ignored', s.sessionCuts == 0)
  s:update(0.2, sample({ 4, 100 }))
  check('a held excursion is a cut', s.sessionCuts == 1 and s.lapCuts == 1)
  s:update(1, sample({ 4, 100 }))
  check('one excursion is one cut', s.sessionCuts == 1)

  s:reset()
  s:update(1, sample({ 2, 100 }))
  s:update(1, sample({ 2, 100 }))
  check('two wheels out is allowed at the default', s.sessionCuts == 0)

  s:reset()
  s:update(1, sample({ 4, 5 }))
  s:update(1, sample({ 4, 5 }))
  check('crawling is ignored', s.sessionCuts == 0)

  s:reset()
  s:update(1, sample({ 4, 100, true }))
  s:update(1, sample({ 4, 100, true }))
  check('the pit lane is ignored', s.sessionCuts == 0)

  s:reset()
  s:update(1, sample({ 4, 100 }))
  s:update(0.3, sample({ 4, 100 }))
  s:update(0.1, sample({ 0, 100 }))
  s:update(0.3, sample({ 4, 100 }))
  check('a quick return is the same cut', s.sessionCuts == 1)

  s:reset()
  s:update(1, sample({ 4, 100 }))
  s:update(0.3, sample({ 4, 100 }))
  s:update(0.7, sample({ 0, 100 }))
  s:update(1, sample({ 4, 100 }))
  s:update(0.3, sample({ 4, 100 }))
  check('coming back later is a new cut', s.sessionCuts == 2)

  s:reset()
  s:update(0.1, sample({ 0, 100, false, 0.5, 0 }))
  s:update(0.1, sample({ 0, 100, false, 0.6, 1 }))
  check('a clean lap counts', s.cleanLaps == 1)
  s:update(1, sample({ 4, 100, false, 0.3, 1 }))
  s:update(0.3, sample({ 4, 100, false, 0.3, 1 }))
  s:update(0.1, sample({ 0, 100, false, 0.6, 2 }))
  check('a lap with a cut breaks the run', s.cleanLaps == 0 and s.lapCuts == 0 and s.bestClean == 1)

  local hot = s:hotspots(1)
  check('hotspots find the cut', #hot == 1 and hot[1].percent == 30 and hot[1].count == 1)

  s:reset()
  check('reset clears everything', s.sessionCuts == 0 and s.cleanLaps == 0 and s.bestClean == 0 and #s:hotspots(3) == 0)
end

local function writeReport()
  io.save(REPORT_PATH, JSON.stringify(report), true)
end

local function takeSample(sim, car, session)
  local window = ac.accessAppWindow(MAIN_WINDOW)
  local grid = {}
  for _, c in ac.iterateCars() do grid[#grid + 1] = { name = ac.getDriverName(c.index), position = c.racePosition } end
  report.sessions[#report.sessions + 1] = {
    index = sim.currentSessionIndex,
    type = session.type,
    name = ac.getSessionName(sim.currentSessionIndex),
    position = car.racePosition,
    cars = sim.carsCount,
    timeLeftMs = sim.sessionTimeLeft,
    laps = session.laps,
    windowOpen = window ~= nil and window:valid() and window:visible(),
    grid = grid,
  }
  report.sessionsCount = sim.sessionsCount
  writeReport()
end

testSteward()
writeReport()

function script.update(dt)
  local sim, car = ac.getSim(), ac.getCar(0)
  if not car or report.finished then return end

  -- Each session opens on the game's menu. This presses its Drive button.
  if sim.isInMainMenu then
    startClock = startClock - dt
    if startClock <= 0 then
      startClock = 2
      ac.tryToStart(true)
    end
    return
  end

  if currentIndex ~= sim.currentSessionIndex then
    currentIndex, sessionClock, sampled, skipClock = sim.currentSessionIndex, 0, false, 0
  end
  sessionClock = sessionClock + dt

  local session = ac.getSession(sim.currentSessionIndex)
  if not session or sessionClock < SETTLE_SECONDS then return end

  if not sampled then
    sampled = true
    takeSample(sim, car, session)
    if session.type == ac.SessionType.Race then
      report.finished = true
      writeReport()
      return
    end
  end

  skipClock = skipClock - dt
  if skipClock <= 0 then
    skipClock = SKIP_RETRY_SECONDS
    ac.tryToSkipSession()
  end
end

function script.windowMain(dt)
  ui.text('Paddock Ladder test helper')
end
