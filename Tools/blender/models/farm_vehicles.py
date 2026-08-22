"""
Farm vehicles and implements - tractor, pickup, hay wagon, plough, harrow.

    blender --background --factory-startup --python Tools/blender/models/farm_vehicles.py

The three vehicles ship as part hierarchies rather than as single meshes, for the reason
the kart README states plainly: anything that moves is its own mesh. A wheel that cannot
turn is the most obviously dead thing you can put on a map, and it is the one piece of
motion that costs nothing to drive from C# - `FarmVehicle` spins each wheel off the
distance the body has travelled, so a towed wagon's wheels are right without anybody
animating them.

**Everything faces +Y.** Blender's +Y is Unity's +Z, which is Unity's forward. Drop one of
these into a scene at rotation zero and it points where the transform says it does.

**Part names are a contract.** `Body` is the root; `Wheel_FL`, `Wheel_FR`, `Wheel_RL` and
`Wheel_RR` are the road wheels, each pivoted on its own hub with its axle along local X;
`Steering` is the steering wheel, pivoted on its column. `FarmVehicle.cs` finds them by
name, so renaming one here silently stops it turning there.

**Wheel radius is not a free number.** The wheels are what the vehicle stands on, so the
hubs are placed exactly a radius up and the assembly is settled onto the ground by
`build_hierarchy(drop_to_ground=True)`. The reason it is settled rather than placed is
that the chamfer decides where a twelve-sided tyre's lowest point actually falls, which is
not a number anybody can write down in advance.

The two implements are static props. They have wheels, but they are ground-driven tools
that spend their life parked in a field, and a plough is much more useful as one mesh with
a collider than as a rig nothing will ever animate.
"""

import math
import os
import random
import sys

import bmesh

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import toebeans_blender as tb  # noqa: E402
import farmyard as fy  # noqa: E402

SEED = 20260820

box = tb.box
beam = tb.beam
prism = tb.prism


def wheel_part(name, hub, radius, width, lugs, rim_fraction, skin_rim=fy.SKIN_METAL,
               segments=12):
    """One road wheel as a Part, authored where it belongs and pivoted on its hub."""
    bm = bmesh.new()
    fy.wheel(bm, hub, (1.0, 0.0, 0.0), radius, width, lugs=lugs,
             rim_fraction=rim_fraction, skin_rim=skin_rim, segments=segments)
    # Spokes across the rim face. Without them a rim is a flat disc, and a flat disc
    # spinning is indistinguishable from a flat disc not spinning.
    spokes = 5
    for i in range(spokes):
        a = math.tau * i / spokes
        r = radius * rim_fraction
        inner = (hub[0], hub[1] + math.cos(a) * radius * 0.16,
                 hub[2] + math.sin(a) * radius * 0.16)
        outer = (hub[0], hub[1] + math.cos(a) * r * 0.94, hub[2] + math.sin(a) * r * 0.94)
        beam(bm, inner, outer, width * 0.34, radius * 0.09, skin_rim, up=(1.0, 0.0, 0.0))
    return tb.Part(name, bm, hub, parent="Body")


def fender(bm, x, hub_y, hub_z, clear, width, skin, steps=7, span=(0.06, 0.94),
           thickness=0.05):
    """An arc of chords over a wheel, standing `clear` off the hub.

    The `up` handed to each chord is the *radial* direction at that chord, not world X,
    and that is the whole subtlety here. `beam` measures its width across `up` cross the
    run: give it world X on a chord lying in the YZ plane and the width comes out radial,
    so every segment grows into a 0.6 m spike and the fender renders as a sunburst. Ask
    for the radial direction and the width goes across the wheel, where a fender's width
    belongs.
    """
    a0, a1 = span
    for i in range(steps):
        b0 = math.pi * (a0 + (a1 - a0) * i / steps)
        b1 = math.pi * (a0 + (a1 - a0) * (i + 1) / steps)
        p0 = (x, hub_y - math.cos(b0) * clear, hub_z + math.sin(b0) * clear)
        p1 = (x, hub_y - math.cos(b1) * clear, hub_z + math.sin(b1) * clear)
        mid = (b0 + b1) * 0.5
        beam(bm, p0, p1, width, thickness, skin,
             up=(0.0, -math.cos(mid), math.sin(mid)))


# --------------------------------------------------------------------------------------
# The tractor
# --------------------------------------------------------------------------------------

class TractorSpec:
    """The tractor's dimensions in one place, so the fenders follow the wheels.

    The proportion is the whole design. A tractor reads as a tractor because the rear
    wheels are enormous and the front ones are not, and because there is a vertical stack
    breaking the line above the bonnet - not because of anything on the bodywork.
    """

    def __init__(self):
        self.rear_r = 0.72
        self.rear_w = 0.42
        self.rear_x = 0.68
        self.rear_y = -0.66

        self.front_r = 0.40
        self.front_w = 0.22
        self.front_x = 0.52
        self.front_y = 1.06

        self.chassis_z = 0.52          # underside of the transmission tunnel
        self.bonnet_z = 0.66           # bonnet floor
        self.bonnet_top = 1.10
        self.bonnet_half = 0.33
        self.nose_y = 1.52
        self.tail_y = -1.24

        self.seat_y = -0.30
        self.seat_z = 1.02
        self.stack_x = 0.27
        self.stack_y = 1.02
        self.stack_top = 2.14

        self.steer_hub = (0.0, 0.30, 1.30)


def add_tractor_body(bm, s):
    # Transmission tunnel and the final drive housings the rear wheels hang off.
    beam(bm, (0.0, s.tail_y + 0.10, s.chassis_z + 0.16), (0.0, s.nose_y - 0.28,
         s.chassis_z + 0.16), 0.46, 0.34, fy.SKIN_METAL, up=(0.0, 0.0, 1.0))
    for sx in (-1, 1):
        beam(bm, (sx * 0.22, s.rear_y, s.rear_r * 0.78),
             (sx * (s.rear_x - s.rear_w * 0.5), s.rear_y, s.rear_r * 0.78),
             0.34, 0.34, fy.SKIN_METAL, up=(0.0, 0.0, 1.0))

    # Bonnet: a tapered box, narrower and lower at the nose. Built as a prism swept along
    # Y so the taper is one solid rather than a stack of shrinking boxes.
    prism(bm, [(-s.bonnet_half, s.stack_y - 0.60, s.bonnet_z),
               (s.bonnet_half, s.stack_y - 0.60, s.bonnet_z),
               (s.bonnet_half * 0.86, s.stack_y - 0.60, s.bonnet_top),
               (-s.bonnet_half * 0.86, s.stack_y - 0.60, s.bonnet_top)],
          (0.0, s.nose_y - (s.stack_y - 0.60) - 0.10, 0.0), fy.SKIN_PAINT)
    # Cowl behind the bonnet, up to the dash.
    box(bm, (-s.bonnet_half, s.stack_y - 0.62, s.bonnet_z),
        (s.bonnet_half, s.seat_y + 0.46, s.bonnet_top + 0.06), fy.SKIN_PAINT)

    # Radiator grille and headlamps - the tractor's face.
    y = s.nose_y - 0.10
    box(bm, (-s.bonnet_half * 0.88, y, s.bonnet_z + 0.02),
        (s.bonnet_half * 0.88, y + 0.10, s.bonnet_top - 0.02), fy.SKIN_RUST)
    for i in range(4):
        z = s.bonnet_z + 0.08 + i * 0.09
        box(bm, (-s.bonnet_half * 0.92, y + 0.06, z),
            (s.bonnet_half * 0.92, y + 0.13, z + 0.045), fy.SKIN_METAL)
    for sx in (-1, 1):
        box(bm, (sx * 0.30 - 0.05, y - 0.02, s.bonnet_top - 0.06),
            (sx * 0.30 + 0.05, y + 0.12, s.bonnet_top + 0.08), fy.SKIN_METAL)
        box(bm, (sx * 0.30 - 0.035, y + 0.11, s.bonnet_top - 0.045),
            (sx * 0.30 + 0.035, y + 0.14, s.bonnet_top + 0.065), fy.SKIN_GLASS)

    # Front axle: a beam under the nose with a kingpin at each end.
    beam(bm, (-s.front_x, s.front_y, s.front_r + 0.06),
         (s.front_x, s.front_y, s.front_r + 0.06), 0.13, 0.13, fy.SKIN_METAL,
         up=(0.0, 0.0, 1.0))
    beam(bm, (0.0, s.front_y, s.front_r + 0.06), (0.0, s.stack_y - 0.30, s.bonnet_z),
         0.16, 0.16, fy.SKIN_METAL, up=(0.0, 0.0, 1.0))

    # Rear fenders: an arc of chords over each wheel, floating clear of the tyre. The
    # gap matters - a fender skinned tight to the tyre reads as a mudguard on a bicycle.
    for sx in (-1, 1):
        fender(bm, sx * (s.rear_x - 0.02), s.rear_y, s.rear_r, s.rear_r + 0.13,
               s.rear_w + 0.16, fy.SKIN_PAINT, steps=7, span=(0.06, 0.94))
        # A step plate, which is how a driver gets up there and what fills the gap
        # between fender and tunnel.
        box(bm, (sx * 0.30, s.rear_y - 0.34, 0.44), (sx * 0.62, s.rear_y - 0.10, 0.50),
            fy.SKIN_METAL)

    # Seat: pan, back and the sprung post under it.
    box(bm, (-0.06, s.seat_y - 0.04, s.seat_z - 0.34),
        (0.06, s.seat_y + 0.06, s.seat_z), fy.SKIN_METAL)
    box(bm, (-0.24, s.seat_y - 0.22, s.seat_z), (0.24, s.seat_y + 0.20, s.seat_z + 0.08),
        fy.SKIN_WOOD_DARK)
    prism(bm, [(-0.24, s.seat_y - 0.26, s.seat_z + 0.06),
               (-0.24, s.seat_y - 0.14, s.seat_z + 0.06),
               (-0.24, s.seat_y - 0.06, s.seat_z + 0.42),
               (-0.24, s.seat_y - 0.20, s.seat_z + 0.42)],
          (0.48, 0.0, 0.0), fy.SKIN_WOOD_DARK)

    # Exhaust stack with a flapper cap. The tallest thing on the vehicle and the reason
    # its silhouette is not a car's.
    fy.lathe(bm, [(0.055, 0.0), (0.055, s.stack_top - s.bonnet_top - 0.10)],
             s.bonnet_top - 0.06, skin=fy.SKIN_RUST, segments=8,
             centre=(s.stack_x, s.stack_y))
    fy.lathe(bm, [(0.085, 0.0), (0.085, 0.09)], s.stack_top - 0.24,
             skin=fy.SKIN_RUST, segments=8, centre=(s.stack_x, s.stack_y))
    with fy.rotated(bm, 26.0, (1.0, 0.0, 0.0), (s.stack_x, s.stack_y, s.stack_top)):
        box(bm, (s.stack_x - 0.08, s.stack_y - 0.09, s.stack_top - 0.02),
            (s.stack_x + 0.08, s.stack_y + 0.09, s.stack_top + 0.02), fy.SKIN_RUST)
    # The air intake stack on the other side, shorter, so the pair is not symmetrical.
    fy.lathe(bm, [(0.05, 0.0), (0.05, 0.52), (0.075, 0.60)], s.bonnet_top - 0.06,
             skin=fy.SKIN_RUST, segments=8, centre=(-s.stack_x, s.stack_y))

    # Steering column, running down to the front axle.
    beam(bm, (0.0, s.steer_hub[1], s.steer_hub[2]), (0.0, s.stack_y - 0.42, s.bonnet_top),
         0.07, 0.07, fy.SKIN_METAL, up=(0.0, 0.0, 1.0))

    # Three-point linkage and drawbar at the back - what makes it a tractor rather than a
    # tall car, and what the implements visibly hitch to.
    for sx in (-1, 1):
        beam(bm, (sx * 0.30, s.tail_y + 0.22, 0.62), (sx * 0.42, s.tail_y, 0.26),
             0.08, 0.09, fy.SKIN_METAL, up=(0.0, 0.0, 1.0))
    beam(bm, (0.0, s.tail_y + 0.30, 0.86), (0.0, s.tail_y + 0.02, 0.74),
         0.07, 0.07, fy.SKIN_METAL, up=(0.0, 0.0, 1.0))
    box(bm, (-0.09, s.tail_y - 0.04, 0.36), (0.09, s.tail_y + 0.34, 0.46), fy.SKIN_RUST)


def build_tractor(name="Farm_Tractor"):
    s = TractorSpec()
    body = bmesh.new()
    add_tractor_body(body, s)

    # Steering wheel, authored about its own hub and tilted back onto the column, the way
    # kart_buggy.py does it. A wheel authored flat turns like a tabletop.
    steer = bmesh.new()
    fy.ring(steer, (0.0, 0.0, 0.0), (0.0, 1.0, 0.0), 0.155, 0.185, 0.035,
            skin=fy.SKIN_WOOD_DARK, segments=12)
    for i in range(3):
        a = math.tau * i / 3.0
        beam(steer, (0.0, 0.0, 0.0), (math.cos(a) * 0.17, 0.0, math.sin(a) * 0.17),
             0.035, 0.022, fy.SKIN_METAL, up=(0.0, 1.0, 0.0))
    fy.lathe(steer, [(0.05, -0.03), (0.05, 0.05)], 0.0, skin=fy.SKIN_METAL, segments=8)
    # The spinner knob, which is the one detail that says "farm" rather than "car".
    fy.lathe(steer, [(0.03, 0.0), (0.042, 0.05), (0.03, 0.08)], 0.0,
             skin=fy.SKIN_WOOD_DARK, segments=8, centre=(0.13, 0.0))
    bmesh.ops.transform(steer, matrix=tb.spin((0, 0, 0), (1, 0, 0), -62.0),
                        verts=list(steer.verts))
    bmesh.ops.translate(steer, verts=list(steer.verts), vec=s.steer_hub)

    parts = [
        tb.Part("Body", body, (0.0, 0.0, 0.0)),
        tb.Part("Steering", steer, s.steer_hub, parent="Body"),
        wheel_part("Wheel_FL", (-s.front_x, s.front_y, s.front_r), s.front_r,
                   s.front_w, 10, 0.50, fy.SKIN_PAINT),
        wheel_part("Wheel_FR", (s.front_x, s.front_y, s.front_r), s.front_r,
                   s.front_w, 10, 0.50, fy.SKIN_PAINT),
        wheel_part("Wheel_RL", (-s.rear_x, s.rear_y, s.rear_r), s.rear_r,
                   s.rear_w, 14, 0.44, fy.SKIN_PAINT),
        wheel_part("Wheel_RR", (s.rear_x, s.rear_y, s.rear_r), s.rear_r,
                   s.rear_w, 14, 0.44, fy.SKIN_PAINT),
    ]
    return parts, fy.finish_parts(parts)


# --------------------------------------------------------------------------------------
# The pickup
# --------------------------------------------------------------------------------------

def build_truck(name="Farm_Truck"):
    """A round-fendered farm pickup with a timber stake bed.

    The fenders are the read. A pickup of this era is a set of separate curved volumes
    bolted to a flat body, and building it as one slab with wheel arches cut in it gives
    you a modern truck, which is the wrong century for a farm.
    """
    s_r, s_w = 0.38, 0.21
    front_y, rear_y = 1.28, -1.24
    hub_x = 0.78
    body = bmesh.new()
    rng = random.Random(SEED + 20)

    floor = 0.52
    half = 0.86

    # Chassis rails and the running boards between the fenders.
    for sx in (-1, 1):
        beam(body, (sx * 0.44, rear_y - 0.42, floor - 0.10),
             (sx * 0.44, front_y + 0.62, floor - 0.10), 0.10, 0.14, fy.SKIN_RUST,
             up=(0.0, 0.0, 1.0))
        box(body, (sx * half - sx * 0.20, -0.62, floor - 0.03),
            (sx * half, 0.46, floor + 0.04), fy.SKIN_WOOD_DARK)

    # Bonnet and cab.
    box(body, (-0.72, 0.44, floor), (0.72, front_y + 0.52, floor + 0.52), fy.SKIN_PAINT_B)
    box(body, (-0.76, -0.66, floor), (0.76, 0.48, floor + 0.56), fy.SKIN_PAINT_B)
    # Cab roof and pillars, with the cabin backed so the windows are not holes.
    box(body, (-0.74, -0.62, floor + 0.52), (0.74, 0.44, floor + 1.16), fy.SKIN_DARK)
    box(body, (-0.78, -0.66, floor + 1.10), (0.78, 0.46, floor + 1.22), fy.SKIN_PAINT_B)
    for sx in (-1, 1):
        for y in (-0.62, 0.40):
            box(body, (sx * 0.74 - 0.07, y - 0.05, floor + 0.50),
                (sx * 0.74 + 0.05, y + 0.05, floor + 1.14), fy.SKIN_PAINT_B)
        box(body, (sx * 0.74 - 0.05, -0.62, floor + 0.50),
            (sx * 0.74 + 0.04, 0.44, floor + 0.62), fy.SKIN_PAINT_B)
    # Windscreen and side glass.
    box(body, (-0.70, 0.40, floor + 0.60), (0.70, 0.45, floor + 1.10), fy.SKIN_GLASS)
    for sx in (-1, 1):
        box(body, (sx * 0.75 - 0.02, -0.58, floor + 0.62),
            (sx * 0.75 + 0.02, 0.36, floor + 1.08), fy.SKIN_GLASS)

    # Grille and lamps.
    y = front_y + 0.52
    box(body, (-0.44, y - 0.02, floor + 0.06), (0.44, y + 0.06, floor + 0.46),
        fy.SKIN_METAL)
    for i in range(5):
        box(body, (-0.40, y + 0.04, floor + 0.10 + i * 0.07),
            (0.40, y + 0.09, floor + 0.13 + i * 0.07), fy.SKIN_PAINT_B)
    for sx in (-1, 1):
        fy.lathe(body, [(0.10, 0.0), (0.11, 0.08), (0.09, 0.12)], floor + 0.34,
                 skin=fy.SKIN_METAL, segments=8, centre=(sx * 0.60, y - 0.06))
        box(body, (sx * 0.60 - 0.08, y - 0.02, floor + 0.28),
            (sx * 0.60 + 0.08, y + 0.04, floor + 0.44), fy.SKIN_GLASS)
    box(body, (-0.78, y + 0.04, floor - 0.02), (0.78, y + 0.12, floor + 0.10),
        fy.SKIN_METAL)

    # Fenders: a swept arc over each wheel, wider than the tyre.
    for sx in (-1, 1):
        for hub_y in (front_y, rear_y):
            fender(body, sx * (hub_x + 0.02), hub_y, s_r, s_r + 0.11,
                   s_w + 0.22, fy.SKIN_PAINT_B, steps=6, span=(0.04, 0.96))

    # The stake bed: a timber floor with slatted sides and a dropped tailgate.
    bed_y0, bed_y1 = rear_y - 0.46, -0.62
    fy.planks(body, bed_y0, bed_y1, 0.0, 0.07, floor + 0.04, floor + 0.10, 7,
              skin=fy.SKIN_WOOD, axis="y")
    for i in range(7):
        yy = bed_y0 + (bed_y1 - bed_y0) * (i + 0.5) / 7.0
        box(body, (-0.80, yy - 0.05, floor + 0.02), (0.80, yy + 0.05, floor + 0.06),
            fy.SKIN_WOOD)
    for sx in (-1, 1):
        for i in range(3):
            yy = bed_y0 + 0.22 + i * ((bed_y1 - bed_y0 - 0.40) / 2.0)
            box(body, (sx * 0.78 - 0.05, yy - 0.045, floor + 0.06),
                (sx * 0.78 + 0.03, yy + 0.045, floor + 0.62), fy.SKIN_WOOD_DARK)
        for z in (floor + 0.20, floor + 0.52):
            box(body, (sx * 0.78 - 0.04, bed_y0, z), (sx * 0.78 + 0.02, bed_y1, z + 0.09),
                fy.SKIN_WOOD)
    box(body, (-0.80, bed_y0 - 0.06, floor + 0.06), (0.80, bed_y0 + 0.02, floor + 0.50),
        fy.SKIN_WOOD)

    # A few bales in the back, so the truck is being used.
    for i in range(3):
        yy = bed_y0 + 0.34 + i * 0.50
        with fy.rotated(body, rng.uniform(-9.0, 9.0), (0.0, 0.0, 1.0), (0.0, yy, 0.0)):
            box(body, (-0.42, yy - 0.22, floor + 0.10), (0.42, yy + 0.22, floor + 0.46),
                fy.SKIN_STRAW)

    parts = [
        tb.Part("Body", body, (0.0, 0.0, 0.0)),
        wheel_part("Wheel_FL", (-hub_x, front_y, s_r), s_r, s_w, 0, 0.56, fy.SKIN_TRIM),
        wheel_part("Wheel_FR", (hub_x, front_y, s_r), s_r, s_w, 0, 0.56, fy.SKIN_TRIM),
        wheel_part("Wheel_RL", (-hub_x, rear_y, s_r), s_r, s_w, 0, 0.56, fy.SKIN_TRIM),
        wheel_part("Wheel_RR", (hub_x, rear_y, s_r), s_r, s_w, 0, 0.56, fy.SKIN_TRIM),
    ]
    return parts, fy.finish_parts(parts)


# --------------------------------------------------------------------------------------
# The hay wagon
# --------------------------------------------------------------------------------------

def build_wagon(name="Farm_HayWagon"):
    """A four-wheeled flatbed wagon with ladder ends, loaded with bales.

    Towable behind the tractor, and the pack's one prop that is longer than a kart, so it
    is also the thing to park across a corner when a track needs a chicane.
    """
    body = bmesh.new()
    rng = random.Random(SEED + 21)
    r_f, r_r = 0.30, 0.38
    hub_x = 0.76
    front_y, rear_y = 1.10, -1.20
    deck = 0.62

    # Chassis and deck.
    for sx in (-1, 1):
        beam(body, (sx * 0.52, rear_y - 0.34, deck - 0.10),
             (sx * 0.52, front_y + 0.50, deck - 0.10), 0.09, 0.16, fy.SKIN_WOOD_DARK,
             up=(0.0, 0.0, 1.0))
    fy.planks(body, rear_y - 0.42, front_y + 0.40, 0.0, 0.07, deck, deck + 0.07, 11,
              skin=fy.SKIN_WOOD, axis="y")
    for i in range(5):
        yy = rear_y - 0.30 + i * ((front_y + 0.30 - (rear_y - 0.30)) / 4.0)
        box(body, (-0.86, yy - 0.055, deck - 0.06), (0.86, yy + 0.055, deck + 0.01),
            fy.SKIN_WOOD_DARK)

    # Ladder ends - the wagon's whole silhouette.
    for sy, yy in ((-1, rear_y - 0.40), (1, front_y + 0.38)):
        for sx in (-1, 1):
            beam(body, (sx * 0.78, yy, deck + 0.02), (sx * 0.70, yy + sy * 0.22,
                 deck + 1.02), 0.07, 0.07, fy.SKIN_WOOD, up=(0.0, 1.0, 0.0))
        for i in range(4):
            z = deck + 0.18 + i * 0.28
            t = (z - deck - 0.02) / 1.00
            box(body, (-0.79, yy + sy * 0.22 * t - 0.035, z),
                (0.79, yy + sy * 0.22 * t + 0.035, z + 0.065), fy.SKIN_WOOD)

    # Side rails.
    for sx in (-1, 1):
        for z in (deck + 0.24, deck + 0.52):
            box(body, (sx * 0.80 - 0.04, rear_y - 0.36, z),
                (sx * 0.80 + 0.03, front_y + 0.34, z + 0.08), fy.SKIN_WOOD)
        for i in range(4):
            yy = rear_y - 0.20 + i * 0.72
            box(body, (sx * 0.80 - 0.045, yy - 0.04, deck), (sx * 0.80 + 0.03,
                yy + 0.04, deck + 0.62), fy.SKIN_WOOD_DARK)

    # Load: two courses of bales, the top one crossed and one bale slipped.
    for level, across in ((0, False), (1, True)):
        z = deck + 0.08 + level * 0.37
        if across:
            for i in range(4):
                yy = rear_y - 0.10 + i * 0.56
                box(body, (-0.72, yy - 0.22, z), (0.72, yy + 0.22, z + 0.36),
                    fy.SKIN_STRAW)
        else:
            for sx in (-1, 1):
                for i in range(2):
                    yy = rear_y + 0.30 + i * 1.00
                    box(body, (sx * 0.06, yy - 0.44, z), (sx * 0.70, yy + 0.44, z + 0.36),
                        fy.SKIN_STRAW)
    with fy.rotated(body, 11.0, (0.0, 1.0, 0.0), (0.0, 0.0, deck + 0.81)):
        box(body, (-0.30, 0.30, deck + 0.81), (0.42, 1.18, deck + 1.17), fy.SKIN_STRAW)
    fy.scatter_seed(body, (0.0, 0.0, 0.0), 1.10, 10, 0.06, fy.SKIN_STRAW, rng)

    # Drawbar, ending in a ring at hitch height.
    beam(body, (0.0, front_y + 0.30, deck - 0.10), (0.0, front_y + 1.06, 0.40),
         0.10, 0.10, fy.SKIN_RUST, up=(0.0, 0.0, 1.0))
    fy.ring(body, (0.0, front_y + 1.10, 0.38), (0.0, 1.0, 0.0), 0.06, 0.11, 0.05,
            skin=fy.SKIN_RUST, segments=8)

    parts = [
        tb.Part("Body", body, (0.0, 0.0, 0.0)),
        wheel_part("Wheel_FL", (-hub_x, front_y, r_f), r_f, 0.16, 0, 0.52, fy.SKIN_RUST),
        wheel_part("Wheel_FR", (hub_x, front_y, r_f), r_f, 0.16, 0, 0.52, fy.SKIN_RUST),
        wheel_part("Wheel_RL", (-hub_x, rear_y, r_r), r_r, 0.18, 0, 0.52, fy.SKIN_RUST),
        wheel_part("Wheel_RR", (hub_x, rear_y, r_r), r_r, 0.18, 0, 0.52, fy.SKIN_RUST),
    ]
    return parts, fy.finish_parts(parts)


# --------------------------------------------------------------------------------------
# Implements
# --------------------------------------------------------------------------------------

def build_plough(name="Farm_Plough"):
    """A three-furrow mounted plough, parked with the shares in the dirt.

    The mouldboards are the whole prop. Each is a twisted plate, faked here as three
    chords at increasing angles - a genuine ruled surface would triple the triangle count
    to sharpen a curve that reads at four metres.
    """
    bm = bmesh.new()
    rng = random.Random(SEED + 22)
    beam_z = 0.74

    # Main beam and the headstock that hitches to the tractor's three-point linkage.
    beam(bm, (0.0, -1.30, beam_z), (0.0, 0.96, beam_z), 0.13, 0.17, fy.SKIN_METAL,
         up=(0.0, 0.0, 1.0))
    for sx in (-1, 1):
        beam(bm, (sx * 0.36, 1.00, 0.30), (sx * 0.10, 0.90, beam_z + 0.10),
             0.09, 0.09, fy.SKIN_METAL, up=(0.0, 0.0, 1.0))
        box(bm, (sx * 0.36 - 0.05, 0.96, 0.24), (sx * 0.36 + 0.05, 1.10, 0.38),
            fy.SKIN_RUST)
    beam(bm, (0.0, 0.90, beam_z + 0.12), (0.0, 1.02, beam_z + 0.46), 0.09, 0.09,
         fy.SKIN_METAL, up=(0.0, 0.0, 1.0))

    for i in range(3):
        y = 0.52 - i * 0.78
        x0 = -0.16 - i * 0.02
        # The leg down from the beam, and the share point that goes in the ground.
        beam(bm, (x0, y, beam_z - 0.06), (x0 - 0.10, y - 0.06, 0.20), 0.09, 0.13,
             fy.SKIN_METAL, up=(0.0, 1.0, 0.0))
        prism(bm, [(x0 - 0.10, y - 0.30, 0.02), (x0 - 0.10, y + 0.18, 0.02),
                   (x0 - 0.10, y + 0.10, 0.26)], (0.06, 0.0, 0.0), fy.SKIN_RUST)
        # Mouldboard: three chords twisting out and up from the share.
        prev = (x0 - 0.06, y - 0.22, 0.06)
        for k, (dx, dz) in enumerate(((0.10, 0.16), (0.22, 0.30), (0.32, 0.40))):
            nxt = (x0 - 0.06 + dx, y - 0.22 + 0.10 * k, 0.06 + dz)
            beam(bm, prev, nxt, 0.36, 0.045, fy.SKIN_RUST, up=(0.0, 1.0, 0.0))
            prev = nxt
        # The coulter disc ahead of each body.
        fy.ring(bm, (x0 + 0.16, y + 0.34, 0.30), (1.0, 0.0, 0.0), 0.02, 0.19, 0.03,
                skin=fy.SKIN_METAL, segments=10)
        beam(bm, (x0 + 0.16, y + 0.34, 0.30), (x0 + 0.02, y + 0.30, beam_z - 0.06),
             0.06, 0.06, fy.SKIN_METAL, up=(0.0, 1.0, 0.0))

    # The depth wheel at the back.
    fy.wheel(bm, (0.30, -1.16, 0.24), (1.0, 0.0, 0.0), 0.24, 0.11, lugs=0,
             rim_fraction=0.52, skin_rim=fy.SKIN_RUST, segments=10)
    beam(bm, (0.30, -1.16, 0.24), (0.06, -1.20, beam_z - 0.04), 0.07, 0.07,
         fy.SKIN_METAL, up=(0.0, 1.0, 0.0))

    # Turned earth where the shares have been sitting.
    fy.scatter_seed(bm, (-0.20, -0.30, 0.0), 0.80, 11, 0.08, fy.SKIN_DIRT, rng)
    return fy.finish(bm, name)


def build_harrow(name="Farm_Harrow"):
    """A trailed disc harrow: two gangs of discs on a square frame.

    Low, wide and flat - which makes it the pack's most dangerous silhouette to leave on
    a racing line, and therefore the one worth having.
    """
    bm = bmesh.new()
    rng = random.Random(SEED + 23)
    frame_z = 0.52
    half = 1.05

    for sy in (-1, 1):
        beam(bm, (-half, sy * 0.46, frame_z), (half, sy * 0.46, frame_z),
             0.10, 0.12, fy.SKIN_METAL, up=(0.0, 0.0, 1.0))
    for sx in (-1, 1):
        beam(bm, (sx * (half - 0.08), -0.46, frame_z), (sx * (half - 0.08), 0.46, frame_z),
             0.09, 0.11, fy.SKIN_METAL, up=(0.0, 0.0, 1.0))

    # Two gangs, angled opposite ways - which is what a disc harrow is for and what stops
    # the two rows reading as one.
    for sy, tilt in ((1, 16.0), (-1, -16.0)):
        with fy.rotated(bm, tilt, (0.0, 0.0, 1.0), (0.0, sy * 0.46, 0.0)):
            beam(bm, (-half + 0.10, sy * 0.46, 0.30), (half - 0.10, sy * 0.46, 0.30),
                 0.06, 0.06, fy.SKIN_RUST, up=(0.0, 0.0, 1.0))
            for i in range(7):
                x = -half + 0.22 + i * ((half * 2 - 0.44) / 6.0)
                fy.ring(bm, (x, sy * 0.46, 0.30), (1.0, 0.0, 0.0), 0.03, 0.27, 0.025,
                        skin=fy.SKIN_METAL, segments=10)
        for i in range(4):
            x = -half + 0.34 + i * ((half * 2 - 0.68) / 3.0)
            beam(bm, (x, sy * 0.46, 0.32), (x, sy * 0.46, frame_z), 0.07, 0.07,
                 fy.SKIN_METAL, up=(0.0, 1.0, 0.0))

    # A-frame drawbar forward, and a weight tray over the gangs.
    for sx in (-1, 1):
        beam(bm, (sx * 0.48, 0.50, frame_z), (0.0, 1.34, frame_z - 0.10),
             0.08, 0.09, fy.SKIN_METAL, up=(0.0, 0.0, 1.0))
    fy.ring(bm, (0.0, 1.38, frame_z - 0.10), (0.0, 1.0, 0.0), 0.05, 0.10, 0.05,
            skin=fy.SKIN_RUST, segments=8)
    box(bm, (-0.62, -0.28, frame_z + 0.06), (0.62, 0.28, frame_z + 0.16), fy.SKIN_METAL)
    for i in range(3):
        box(bm, (-0.34 + i * 0.28, -0.16, frame_z + 0.16),
            (-0.14 + i * 0.28, 0.16, frame_z + 0.34), fy.SKIN_RUST)

    fy.scatter_seed(bm, (0.0, 0.0, 0.0), 1.00, 10, 0.07, fy.SKIN_DIRT, rng)
    return fy.finish(bm, name)


if __name__ == "__main__":
    manifest = fy.Manifest("farm_vehicles")

    for builder, model, budget, note in (
        (build_tractor, "Farm_Tractor", 10800,
         "wheels spin; Wheel_F* steer; Steering turns about its own hub"),
        (build_truck, "Farm_Truck", 9200, "wheels spin; Wheel_F* steer"),
        (build_wagon, "Farm_HayWagon", 8400, "towable; wheels spin off distance travelled"),
    ):
        tb.fresh_scene()
        parts, palette = builder()
        stats = tb.build_hierarchy(parts, model, palette,
                                   chamfer_m=fy.CHAMFER, max_tris=budget,
                                   max_size_m=6.0, drop_to_ground=True)
        # A box, not the mesh. A vehicle's mesh is six meshes, four of which spin, and a
        # rotating non-convex MeshCollider is both the most expensive collider Unity has
        # and physically meaningless. A parked tractor only has to be solid.
        manifest.add(stats, palette, collider=fy.COLLIDER_BOX, tag="vehicle", note=note)

    for builder, budget in ((build_plough, 3700), (build_harrow, 6600)):
        tb.fresh_scene()
        obj, palette = builder()
        stats = tb.build(obj, obj.name, max_tris=budget, max_size_m=4.0)
        manifest.add(stats, palette, tag="implement")

    manifest.write()
