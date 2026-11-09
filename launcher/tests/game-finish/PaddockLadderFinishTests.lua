-- Runs inside the real game during run-finish-test.ps1. It drives the car to the flag, then records whether
-- the car went to its pit box and stayed there. The script presses the keys and clicks the results screen.

local DATA_DIR = ac.getFolder(ac.FolderID.ACDocuments) .. '/paddock_ladder'
local LOCK_SETTLE_SECONDS = 4

local text = io.load(DATA_DIR .. '/race_context.json')
local context = text and JSON.parse(text) or {}
local reportPath = DATA_DIR .. '/finish_test_' .. tostring(context.RoundNumber) .. '.json'
local report = { round = context.RoundNumber, raceId = context.RaceId, stage = 'wait', maxSpeedLocked = 0 }
local stageClock, saveClock = 0, 0
report.steerMax, report.steerNow = 0, 0
report.rivalsCalm, report.rivalsAngry = 0, 0

-- Aggression reads back as 0 to 1. Grudge holders start at 10% and only rise when they're close.
local function countRivals()
  local calm, angry = 0, 0
  for i = 1, ac.getSim().carsCount - 1 do
    local other = ac.getCar(i)
    if other and other.aiAggression > 0.3 then angry = angry + 1 else calm = calm + 1 end
  end
  report.rivalsAngry = math.max(report.rivalsAngry, angry)
  report.rivalsCalm = math.max(report.rivalsCalm, calm)
end

local function enter(stage)
  report.stage = stage
  stageClock = 0
end

function script.update(dt)
  local sim, car = ac.getSim(), ac.getCar(0)
  if not car then return end
  stageClock = stageClock + dt
  if report.stage == 'driving' then countRivals() end

  if report.stage == 'wait' and not sim.isInMainMenu then report.steerMax = math.max(report.steerMax, math.abs(car.steer)) end
  if report.stage == 'wait' then
    if sim.isInMainMenu then
      if stageClock > 3 then
        stageClock = 0
        ac.tryToStart(true)
      end
    elseif sim.isSessionStarted or stageClock > 12 then
      report.carriedBody = car.damage[0]
      report.carriedEngine = car.engineLifeLeft
      report.steerNow = car.steer
      physics.setCarAutopilot(true, false)
      enter('driving')
    end
  elseif report.stage == 'driving' then
    if car.isRaceFinished then
      physics.setCarAutopilot(false, false)
      report.position = car.racePosition
      enter('flag')
    end
  elseif report.stage == 'flag' then
    if stageClock > LOCK_SETTLE_SECONDS then enter('locked') end
  elseif report.stage == 'locked' then
    report.maxSpeedLocked = math.max(report.maxSpeedLocked, car.speedKmh)
    report.inPit = car.isInPit
    report.debrief = io.fileExists(DATA_DIR .. '/race_debrief.json')
    report.musicTime = ac.load('paddockladder.musicTime') or 0
    report.screen = ac.load('paddockladder.screen') or ''
    report.soundHeard = ac.load('paddockladder.soundHeard') or 0
    report.soundCount = ac.load('paddockladder.soundCount') or 0
  end

  saveClock = saveClock - dt
  if saveClock <= 0 then
    saveClock = 0.2
    io.save(reportPath, JSON.stringify(report), true)
  end
end

function script.windowMain(dt)
  ui.text('Paddock Ladder finish test: ' .. report.stage)
end
