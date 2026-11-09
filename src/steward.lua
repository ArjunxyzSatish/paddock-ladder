-- Paddock Ladder: track-limits steward. Pure Lua, fed one sample per frame.

local M = {}
local S = {}
S.__index = S

local BINS = 100
local ARM_SECONDS = 0.2
local REARM_SECONDS = 0.6
local MIN_SPEED_KMH = 15

function M.new(tyresOut)
  local self = setmetatable({ tyresOut = tyresOut or 3 }, S)
  self:reset()
  return self
end

function S:reset()
  self.lapCuts = 0
  self.sessionCuts = 0
  self.cleanLaps = 0
  self.bestClean = 0
  self.flash = 0
  self.outsideFor = 0
  self.insideFor = 0
  self.inCut = false
  self.lastLap = nil
  self.bins = {}
  for i = 1, BINS do self.bins[i] = 0 end
end

-- input: { wheelsOutside=, speedKmh=, inPitlane=, spline=, lapCount= }. Returns true on a new cut.
function S:update(dt, input)
  self.flash = math.max(0, self.flash - dt * 1.2)

  if self.lastLap == nil then
    self.lastLap = input.lapCount
  elseif input.lapCount ~= self.lastLap then
    if input.lapCount > self.lastLap then
      if self.lapCuts == 0 then
        self.cleanLaps = self.cleanLaps + 1
        self.bestClean = math.max(self.bestClean, self.cleanLaps)
      else
        self.cleanLaps = 0
      end
    end
    self.lapCuts = 0
    self.lastLap = input.lapCount
  end

  local outside = input.wheelsOutside >= self.tyresOut
    and input.speedKmh > MIN_SPEED_KMH
    and not input.inPitlane

  if not outside then
    self.insideFor = self.insideFor + dt
    self.outsideFor = 0
    if self.inCut and self.insideFor >= REARM_SECONDS then self.inCut = false end
    return false
  end

  self.outsideFor = self.outsideFor + dt
  self.insideFor = 0
  if self.inCut or self.outsideFor < ARM_SECONDS then return false end

  self.inCut = true
  self.lapCuts = self.lapCuts + 1
  self.sessionCuts = self.sessionCuts + 1
  self.flash = 1

  local bin = math.floor(input.spline * BINS) + 1
  bin = math.max(1, math.min(BINS, bin))
  self.bins[bin] = self.bins[bin] + 1
  return true
end

-- The worst spots on the lap, as { percent=, count= }, most frequent first.
function S:hotspots(limit)
  local list = {}
  for i = 1, BINS do
    if self.bins[i] > 0 then list[#list + 1] = { percent = i - 1, count = self.bins[i] } end
  end
  table.sort(list, function(a, b)
    if a.count ~= b.count then return a.count > b.count end
    return a.percent < b.percent
  end)
  while #list > (limit or 3) do list[#list] = nil end
  return list
end

return M
