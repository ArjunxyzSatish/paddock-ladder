-- Decides who caused a contact, from where both cars were and how they were moving just before it.
-- It only works with plain numbers, so every kind of contact can be tested on its own.

local Fault = {}

local NOSE_TO_TAIL_METRES = 2.6
local CLEAR_SPEED_GAP = 0.7       -- metres per second one car must be closing faster to take the blame
local CLEAR_DRIFT_GAP = 0.6       -- metres per second one car must be moving sideways faster
local STOPPED_SPEED = 3
local SLIDING_ALIGNMENT = 0.75    -- below this, the car isn't travelling the way it's pointing
local WHEELS_OFF_TRACK = 3

local function dot(ax, az, bx, bz)
  return ax * bx + az * bz
end

local function speedOf(car)
  return math.sqrt(car.vx * car.vx + car.vz * car.vz)
end

local function isOutOfControl(car)
  local speed = speedOf(car)
  if speed < STOPPED_SPEED then return true end
  return dot(car.vx / speed, car.vz / speed, car.lookX, car.lookZ) < SLIDING_ALIGNMENT
end

-- me and other: x, z, vx, vz, lookX, lookZ, sideX, sideZ. Returns who ('you', 'them' or 'shared') and how ('rear' or 'side').
function Fault.rule(me, other, wheelsOffTrack)
  local dx, dz = other.x - me.x, other.z - me.z
  local ahead = dot(dx, dz, me.lookX, me.lookZ)
  local closing = dot(me.vx, me.vz, me.lookX, me.lookZ) - dot(other.vx, other.vz, me.lookX, me.lookZ)

  if ahead >= NOSE_TO_TAIL_METRES then
    return closing > CLEAR_SPEED_GAP and 'you' or 'shared', 'rear'
  end
  if ahead <= -NOSE_TO_TAIL_METRES then
    -- A car that has spun or stopped can't expect the driver behind to take all the blame.
    if closing < -CLEAR_SPEED_GAP and not isOutOfControl(me) then return 'them', 'rear' end
    return 'shared', 'rear'
  end

  -- Coming back onto the track is the driver's responsibility.
  if (wheelsOffTrack or 0) >= WHEELS_OFF_TRACK then return 'you', 'side' end

  local across = dot(dx, dz, me.sideX, me.sideZ)
  local toward = across >= 0 and 1 or -1
  local myDrift = dot(me.vx, me.vz, me.sideX, me.sideZ) * toward
  local theirDrift = dot(other.vx, other.vz, me.sideX, me.sideZ) * -toward
  local gap = myDrift - theirDrift
  if gap > CLEAR_DRIFT_GAP then return 'you', 'side' end
  if gap < -CLEAR_DRIFT_GAP then return 'them', 'side' end
  return 'shared', 'side'
end

return Fault
