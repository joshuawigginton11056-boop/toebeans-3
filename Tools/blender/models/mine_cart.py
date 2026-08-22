"""
Mine cart - the cave kart. A riveted tub on flanged rail wheels, lit by a carbide lamp.

    blender --background --factory-startup --python Tools/blender/models/mine_cart.py

This is the one concept in the set whose silhouette hook also earns gameplay: the carbide
lamp is the tallest thing on the kart *and* the light that makes a dark map drivable. That
only works if the lamp is where the C# expects a lamp to be, which turned out to be the
single constraint that shaped the whole model.

**The lamp is the roof bar, not a lamp on the hoop.** KartLights does not hang a Light
wherever a style drew a lamp; it hangs them on KartBlueprint's own points - the nose pair,
and one wide light at `RoofBarLightCentre` for the pod bar. A carbide lamp on the roll hoop
at z -0.72 would be a mile from any of them and would light nothing. So the lamp sits at
the roof pod centre (y 1.33, z 0.22) where `RoofBarLightCentre` already is, carried forward
of the hoop on two rails, and the style turns the nose pair off instead of building
headlamps it has no housings for. One big lamp where four pods would have gone.

The rails run at x +/-0.30 rather than up the middle because KartBlueprint's helmet is a
0.125 m sphere at (0, 1.18, -0.55): a centre rail at y 1.33 clears the driver's head by
25 mm, which is the kind of margin that survives until somebody nudges the hoop.

**"KartRubber barely visible" is the wheel brief.** A rail wheel is iron, so the rubber slot
is one thin dark band buried in the tread and the flange does the work. The flange is drawn
at exactly the nominal radius and the tread just under it, so the flange is what touches the
ground - which is what a rail wheel looks like, and it keeps `assert_tread` satisfied.
"""

import math
import os
import sys

import bmesh
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import kartworks as kw  # noqa: E402
import toebeans_blender as tb  # noqa: E402
from kartworks import u, usize, mirrored  # noqa: E402

BODY_NAME = "KartMine_Body"
WHEEL_FRONT_NAME = "KartMine_WheelFront"
WHEEL_REAR_NAME = "KartMine_WheelRear"
STEERING_WHEEL_NAME = "KartMine_SteeringWheel"

FRONT_WHEEL_RADIUS = kw.FRONT_WHEEL_RADIUS
REAR_WHEEL_RADIUS = kw.REAR_WHEEL_RADIUS
FRONT_TRACK = kw.FRONT_TRACK
REAR_TRACK = kw.REAR_TRACK
FRONT_AXLE_Z = kw.FRONT_AXLE_Z
REAR_AXLE_Z = kw.REAR_AXLE_Z
STEERING_HUB = kw.STEERING_HUB
STEERING_RACK = kw.STEERING_RACK

FRAME, BODY, SEAT, RIM, RUBBER, LENS = range(6)

IRON = FRAME        # hoop, mast, brackets, axle - the dark structural iron
PLATE = BODY        # the riveted steel tub
WOOD = SEAT         # slat sides, seat plank, lamp handle
BRIGHT = RIM        # rivets, flanges, the lamp's reflector - worn to a shine
BAND = RUBBER       # the one thin dark band on each wheel

# Metallic is kept low deliberately, and it is not a preview-only concern.
#
# The first cut of this palette ran 0.70-0.85 metallic on the iron, on the reasoning that
# iron is metal. It rendered the wheels and the tub near-black: a metal surface has almost
# no diffuse response, so it shows you the environment, and at a grazing angle - which is
# most of a cylinder - there is nothing to show. The buggy gets away with 0.9 on KartRim
# because that slot is small flat lamp housings facing the sun, not whole wheels.
#
# Flat-shaded faceted geometry wants low metallic and its colour in the albedo, which is
# also what makes a style's palette legible at chase-camera distance.
PALETTE = kw.palette(
    frame=((0.21, 0.22, 0.25), 0.35, 0.60),      # KartFrame  - blackened iron
    body=((0.47, 0.51, 0.57), 0.30, 0.55),       # KartBody   - riveted steel plate
    seat=((0.42, 0.28, 0.16), 0.00, 0.80),       # KartSeat   - pit timber
    rim=((0.63, 0.62, 0.57), 0.40, 0.45),        # KartRim    - iron worn bright
    rubber=((0.10, 0.10, 0.11), 0.00, 0.85),     # KartRubber - barely visible
)

# ---------------------------------------------------------------------------------------
# Layout
# ---------------------------------------------------------------------------------------

FLOOR_TOP_Y = 0.22
TUB_X = 0.42
# Held at 0.62, not the 0.74 this started at. A skip deep enough to look like a skip puts
# its capping rail above the driver's elbows and turns the cockpit into a hole - the render
# showed a bench seat sunk out of sight behind a wall. The driver is primitives placed by
# KartBlueprint and cannot lean out of it, so the wall comes down instead.
TUB_TOP_Y = 0.62
# Stops short of the front axle so the kart has a nose rather than a full-length bed. At
# 1.08 the tub ran past the front wheels and read as a pickup truck.
TUB_FRONT_Z = 0.88
TUB_BACK_Z = -0.98
PLATE_T = 0.045

# The tub flares outward as it rises, the way a real skip does. Small, but it is what
# stops the body reading as a plain box on wheels.
FLARE = 0.05

MAST_X = 0.30
LAMP_Y = kw.ROOF_POD_Y
LAMP_Z = kw.ROOF_POD_Z
LAMP_RADIUS = 0.13

MAIN_TUBE = 0.042
BRACE_TUBE = 0.030
THIN_TUBE = 0.022


# ---------------------------------------------------------------------------------------
# Body
# ---------------------------------------------------------------------------------------

def add_floor(bm):
    tb.cuboid(bm, u(0.0, FLOOR_TOP_Y - 0.025, 0.05), usize(0.84, 0.05, 1.60), IRON)


def add_tub(bm):
    """The skip: four flared steel walls, a capping rail and a lot of rivets."""
    mid_z = (TUB_FRONT_Z + TUB_BACK_Z) * 0.5

    for side, _ in mirrored((0.0, 0.0, 0.0)):
        # Side wall, leaning out as it rises.
        tb.beam(bm, u(TUB_X * side, FLOOR_TOP_Y, mid_z),
                u((TUB_X + FLARE) * side, TUB_TOP_Y, mid_z),
                TUB_FRONT_Z - TUB_BACK_Z, PLATE_T, PLATE,
                up=u(1.0 * side, 0.0, 0.0))
        # Capping rail along the top edge.
        tb.beam(bm, u((TUB_X + FLARE) * side, TUB_TOP_Y, TUB_BACK_Z),
                u((TUB_X + FLARE) * side, TUB_TOP_Y, TUB_FRONT_Z), 0.10, 0.06, BRIGHT)

    # Front and back walls, inset so the side walls read as the outer skin.
    for z, height in ((TUB_FRONT_Z, 0.34), (TUB_BACK_Z, 0.40)):
        tb.cuboid(bm, u(0.0, FLOOR_TOP_Y + height * 0.5, z),
                  usize(TUB_X * 2, height, PLATE_T), PLATE)
        tb.cuboid(bm, u(0.0, FLOOR_TOP_Y + height, z),
                  usize(TUB_X * 2 + 0.06, 0.06, 0.09), BRIGHT)


def add_nose(bm):
    """The prow ahead of the tub.

    Once the tub stops short of the front axle the kart needs something over the front
    wheels or it ends in mid-air. A short riveted snout, dropping and narrowing forward,
    which also gives the front of this kart a different read from the buggy's tube prow.
    """
    for side, _ in mirrored((0.0, 0.0, 0.0)):
        tb.beam(bm, u((TUB_X - 0.01) * side, 0.40, TUB_FRONT_Z),
                u(0.24 * side, 0.34, 1.24), 0.05, 0.26, PLATE)
    tb.slab(bm, u(0.0, 0.47, TUB_FRONT_Z), u(0.0, 0.44, 1.22), 0.52, 0.05, PLATE)
    tb.cuboid(bm, u(0.0, 0.34, 1.24), usize(0.52, 0.24, 0.07), PLATE)
    tb.cuboid(bm, u(0.0, 0.47, 1.24), usize(0.56, 0.06, 0.10), BRIGHT)

    # A coupling hook off the prow, because this thing is descended from something towed.
    tb.tube(bm, u(0.0, 0.30, 1.24), u(0.0, 0.30, 1.34), 0.030, IRON, segments=6)
    tb.tube(bm, u(0.0, 0.30, 1.34), u(0.0, 0.36, 1.36), 0.024, BRIGHT, segments=6)


def add_slats(bm):
    """Wooden slats down the outside of each wall - the pit-timber half of the design.

    Vertical rather than horizontal. Horizontal slats on a body this shallow read as
    stripes painted on a box; vertical ones break the wall into pieces and give the flare
    something to be visible against.
    """
    for side, _ in mirrored((0.0, 0.0, 0.0)):
        for z in (-0.86, -0.64, -0.42, -0.20, 0.02, 0.24, 0.46, 0.68):
            tb.beam(bm, u((TUB_X + 0.012) * side, FLOOR_TOP_Y + 0.03, z),
                    u((TUB_X + FLARE + 0.012) * side, TUB_TOP_Y - 0.04, z),
                    0.15, 0.035, WOOD, up=u(1.0 * side, 0.0, 0.0))


def add_rivets(bm):
    """Rivet heads along the seams. Four-sided studs, because at this size that is all
    that survives - a rounder rivet costs triangles and reads identically."""
    for side, _ in mirrored((0.0, 0.0, 0.0)):
        for z in (-0.90, -0.50, -0.10, 0.30, 0.70, 0.86):
            for y in (FLOOR_TOP_Y + 0.06, TUB_TOP_Y - 0.08):
                lift = (y - FLOOR_TOP_Y) / (TUB_TOP_Y - FLOOR_TOP_Y)
                x = (TUB_X + FLARE * lift) * side
                tb.tube(bm, u(x, y, z), u(x + 0.026 * side, y, z), 0.020, BRIGHT,
                        segments=4)


def add_hoop_and_mast(bm):
    """Roll hoop at the blueprint's own point, and the rails carrying the lamp forward."""
    half = kw.ROLL_HOOP_HALF_WIDTH
    top = kw.ROLL_HOOP_TOP_Y
    z = kw.ROLL_HOOP_Z

    for side, _ in mirrored((0.0, 0.0, 0.0)):
        tb.tube(bm, u(half * side, FLOOR_TOP_Y + 0.10, z), u(half * side, top, z),
                MAIN_TUBE, IRON)
        tb.tube(bm, u(half * side, top - 0.08, z),
                u((half - 0.04) * side, FLOOR_TOP_Y + 0.34, TUB_BACK_Z + 0.02),
                BRACE_TUBE, IRON)
        # Forward rail to the lamp. Outboard of the driver's helmet by design.
        tb.tube(bm, u(half * side, top - 0.03, z), u(MAST_X * side, LAMP_Y, LAMP_Z),
                BRACE_TUBE, IRON)
        # Strut down from the rail into the tub, so the lamp is not on a diving board.
        tb.tube(bm, u(MAST_X * side, LAMP_Y - 0.02, LAMP_Z),
                u((TUB_X + FLARE - 0.04) * side, TUB_TOP_Y, LAMP_Z + 0.10),
                THIN_TUBE, IRON)

    tb.tube(bm, u(-half, top, z), u(half, top, z), MAIN_TUBE, IRON)


def add_carbide_lamp(bm):
    """The lamp. A flared reflector on a bracket, with its glass on RoofBarLightCentre.

    The glass has to sit where KartBlueprint says, because KartLights hangs the actual
    Unity Light on the front face of it - see the module docstring. Everything else about
    the lamp is free, so it is a proper flared carbide reflector rather than a pod.
    """
    back_z = LAMP_Z - 0.09
    mouth_z = LAMP_Z + (kw.ROOF_POD_SIZE[2] + kw.LENS_THICKNESS) * 0.5

    tb.tube(bm, u(-MAST_X, LAMP_Y, LAMP_Z), u(MAST_X, LAMP_Y, LAMP_Z), 0.022, IRON)

    # Body of the lamp - the carbide generator it sits on - then the reflector.
    #
    # Drawn deep rather than shallow. The first cut flared 55 mm to 130 mm over 130 mm of
    # length, which from the chase camera is a flat disc seen face-on: it read as a
    # satellite dish stuck on a pole. A long cone reads as a reflector from every angle
    # because you can see down into it, and the bezel gives the mouth an edge.
    tb.tube(bm, u(0.0, LAMP_Y, back_z - 0.13), u(0.0, LAMP_Y, back_z), 0.052, IRON,
            segments=8)
    tb.taper(bm, u(0.0, LAMP_Y, back_z), u(0.0, LAMP_Y, mouth_z - kw.LENS_THICKNESS),
             0.045, LAMP_RADIUS, BRIGHT, segments=8)
    # No bezel ring around the mouth. One was tried at LAMP_RADIUS + 16 mm and it sat
    # exactly over the reflector's rim, hiding the flare that was the point of drawing a
    # cone at all - the lamp went back to reading as a plate on a stick. The taper's own
    # rim is the edge, and the glass is set in behind it so the mouth has visible depth.

    # The glass. Its own slot, because Unity switches the lamp on by swapping the material
    # on exactly these faces - and it is the front face of this box that KartLights hangs
    # the actual Light on, which is why it sits on RoofBarLightCentre and not where it looks
    # best.
    tb.tube(bm, u(0.0, LAMP_Y, mouth_z - kw.LENS_THICKNESS), u(0.0, LAMP_Y, mouth_z),
            LAMP_RADIUS * 0.86, LENS, segments=8)

    # Wooden carry handle over the top, and the flint striker on the side.
    tb.tube(bm, u(-0.05, LAMP_Y + 0.07, back_z - 0.02),
            u(0.05, LAMP_Y + 0.07, back_z - 0.02), 0.016, WOOD, segments=4)
    for side, _ in mirrored((0.0, 0.0, 0.0)):
        tb.tube(bm, u(0.05 * side, LAMP_Y + 0.02, back_z - 0.02),
                u(0.05 * side, LAMP_Y + 0.07, back_z - 0.02), 0.012, IRON, segments=4)
    tb.cuboid(bm, u(0.07, LAMP_Y - 0.04, back_z - 0.03), usize(0.05, 0.05, 0.06), BRIGHT)


def add_cockpit(bm):
    """A plank bench rather than a bucket - this kart has no racing seat in it."""
    tb.slab(bm, u(0.0, 0.36, -0.18), u(0.0, 0.38, -0.60), 0.48, 0.07, WOOD)
    tb.slab(bm, u(0.0, 0.42, -0.62), u(0.0, 0.94, -0.78), 0.44, 0.07, WOOD)
    for _side, (low, high) in mirrored((0.24, 0.40, -0.24), (0.24, 0.86, -0.74)):
        tb.slab(bm, low, high, 0.05, 0.12, WOOD)

    # Iron strapping across the bench back, which is what makes it read as pit furniture.
    for y in (0.56, 0.78):
        tb.cuboid(bm, u(0.0, y, -0.70), usize(0.46, 0.04, 0.05), IRON)

    tb.cuboid(bm, u(0.0, 0.60, 0.44), usize(0.46, 0.09, 0.20), PLATE)
    tb.tube(bm, u(*STEERING_RACK), u(*STEERING_HUB), THIN_TUBE, IRON)
    tb.tube(bm, u(-0.16, STEERING_RACK[1], STEERING_RACK[2]),
            u(0.16, STEERING_RACK[1], STEERING_RACK[2]), 0.028, IRON)
    for side, _ in mirrored((0.0, 0.0, 0.0)):
        tb.tube(bm, u(0.06 * side, 0.30, 0.32), u(0.50 * side, 0.28, 0.76), 0.016, IRON)
        tb.slab(bm, u(0.17 * side, 0.24, 0.68), u(0.17 * side, 0.36, 0.64), 0.10, 0.03, BRIGHT)

    # Brake lever - a long iron handle, the mine cart's other period tell.
    tb.tube(bm, u(0.34, 0.38, -0.10), u(0.30, 0.78, -0.20), 0.020, IRON)
    tb.tube(bm, u(0.30, 0.78, -0.20), u(0.30, 0.84, -0.20), 0.030, WOOD, segments=6)


def add_drivetrain(bm):
    """A boiler-ish drum behind the bench, and the axle."""
    tb.tube(bm, u(-0.22, 0.52, -0.92), u(0.22, 0.52, -0.92), 0.19, PLATE, segments=8)
    tb.tube(bm, u(-0.24, 0.52, -0.92), u(-0.26, 0.52, -0.92), 0.20, BRIGHT, segments=8)
    tb.tube(bm, u(0.24, 0.52, -0.92), u(0.26, 0.52, -0.92), 0.20, BRIGHT, segments=8)
    # Chimney, short - the lamp owns the tall slot on this kart and nothing may compete.
    tb.tube(bm, u(0.0, 0.70, -0.92), u(0.0, 0.90, -0.94), 0.045, IRON, segments=6)
    tb.tube(bm, u(0.0, 0.90, -0.94), u(0.0, 0.94, -0.94), 0.060, BRIGHT, segments=6)

    tb.tube(bm, u(-0.62, REAR_WHEEL_RADIUS, REAR_AXLE_Z),
            u(0.62, REAR_WHEEL_RADIUS, REAR_AXLE_Z), 0.042, IRON)
    tb.tube(bm, u(-0.58, FRONT_WHEEL_RADIUS, FRONT_AXLE_Z),
            u(0.58, FRONT_WHEEL_RADIUS, FRONT_AXLE_Z), 0.036, IRON)


def add_suspension(bm):
    """Leaf-ish stacks rather than coil-overs: a mine cart has no business with springs
    that look modern, but it still has to cover 280 mm of travel visually."""
    for side, _ in mirrored((0.0, 0.0, 0.0)):
        tb.tube(bm, u(0.28 * side, FLOOR_TOP_Y, FRONT_AXLE_Z),
                u(0.52 * side, FRONT_WHEEL_RADIUS, FRONT_AXLE_Z), BRACE_TUBE, IRON)
        tb.tube(bm, u(0.30 * side, 0.30, -0.48),
                u(0.56 * side, REAR_WHEEL_RADIUS, REAR_AXLE_Z), 0.032, IRON)
        # Leaf stacks, sitting directly on each hub and tucked under the tub's flare.
        #
        # These used to hang at x +/-0.50 and y 0.62-0.74, which is outboard of the body
        # and above the tyre: three bright plates floating in mid-air beside the kart with
        # nothing holding them. A leaf spring reads as suspension only when you can see
        # both ends of it land on something, so they sit low over the hub now, with the
        # shackle running up into the tub wall.
        for i, lift in enumerate((0.10, 0.145, 0.19)):
            span = 0.34 - i * 0.06
            tb.cuboid(bm, u(0.46 * side, FRONT_WHEEL_RADIUS + lift, FRONT_AXLE_Z),
                      usize(0.07, 0.022, span), BRIGHT)
            tb.cuboid(bm, u(0.50 * side, REAR_WHEEL_RADIUS + lift, REAR_AXLE_Z),
                      usize(0.07, 0.022, span), BRIGHT)
        tb.tube(bm, u(0.46 * side, FRONT_WHEEL_RADIUS + 0.19, FRONT_AXLE_Z),
                u((TUB_X + 0.02) * side, FLOOR_TOP_Y + 0.08, FRONT_AXLE_Z - 0.10),
                THIN_TUBE, IRON)
        tb.tube(bm, u(0.50 * side, REAR_WHEEL_RADIUS + 0.19, REAR_AXLE_Z),
                u((TUB_X + 0.02) * side, FLOOR_TOP_Y + 0.08, REAR_AXLE_Z + 0.12),
                THIN_TUBE, IRON)


def build_body():
    bm = bmesh.new()
    add_floor(bm)
    add_tub(bm)
    add_nose(bm)
    add_slats(bm)
    add_rivets(bm)
    add_hoop_and_mast(bm)
    add_carbide_lamp(bm)
    add_cockpit(bm)
    add_drivetrain(bm)
    add_suspension(bm)
    return kw.finish(bm, BODY_NAME, PALETTE)


# ---------------------------------------------------------------------------------------
# Wheels
# ---------------------------------------------------------------------------------------

def build_wheel(name, radius, width):
    """A flanged rail wheel: iron tread, one deep flange, spoked face.

    The flange sits at exactly the nominal radius and the tread just inside it, so the
    flange is what meets the ground - which is both what a rail wheel looks like and what
    keeps the wheel out of the road. See kartworks.assert_tread.
    """
    bm = bmesh.new()

    # Tread band, just under the radius.
    tb.tube(bm, u(-width * 0.42, 0, 0), u(width * 0.42, 0, 0), radius * 0.93, BRIGHT,
            segments=14)
    # The one thin dark band - all the rubber this design gets. Narrow: at 0.20 of the
    # width it was a black stripe down the middle of every wheel and the kart read as
    # running on tyres after all, which is the opposite of the brief.
    tb.tube(bm, u(-width * 0.06, 0, 0), u(width * 0.06, 0, 0), radius * 0.945, BAND,
            segments=14)
    # Flange, inboard, at the full radius.
    tb.tube(bm, u(-width * 0.50, 0, 0), u(-width * 0.40, 0, 0), radius, BRIGHT,
            segments=14)

    # Web and hub.
    tb.tube(bm, u(-width * 0.16, 0, 0), u(width * 0.16, 0, 0), radius * 0.60, RIM,
            segments=12)
    tb.tube(bm, u(-width * 0.60, 0, 0), u(width * 0.60, 0, 0), radius * 0.20, IRON,
            segments=6)

    # Straight spokes across the outer face - a cast iron wheel, not a pressed one.
    for _i, _theta, spoke in kw.around(6, radius * 0.52):
        tb.slab(bm, u(width * 0.30, 0.0, 0.0), u(width * 0.30, spoke.y, spoke.z),
                width * 0.12, radius * 0.13, RIM)

    # Bolt heads round the hub.
    for _i, _theta, bolt in kw.around(6, radius * 0.32):
        tb.tube(bm, u(width * 0.34, bolt.y, bolt.z), u(width * 0.40, bolt.y, bolt.z),
                radius * 0.055, BRIGHT, segments=4)

    return kw.finish(bm, name, PALETTE)


def build_steering_wheel(name=STEERING_WHEEL_NAME):
    """A capstan handwheel - the brake wheel off a mine skip, not a racing rim.

    Authored in the "Steering" pivot's local space: rim in local XZ, column up local Y.
    """
    bm = bmesh.new()
    radius = kw.STEERING_WHEEL_RADIUS
    segments = kw.STEERING_RIM_SEGMENTS

    ring = [(radius * math.cos(2.0 * math.pi * i / segments), 0.0,
             radius * math.sin(2.0 * math.pi * i / segments)) for i in range(segments)]
    for i, point in enumerate(ring):
        tb.tube(bm, u(*point), u(*ring[(i + 1) % segments]), 0.024, BRIGHT, segments=5)

    tb.tube(bm, u(0.0, -0.03, 0.0), u(0.0, 0.03, 0.0), 0.050, IRON, segments=8)

    # Five flat spokes, which is what makes it read as cast rather than fabricated.
    for i in range(5):
        phi = math.radians(90.0 + i * 72.0)
        spoke = (radius * math.cos(phi), 0.0, radius * math.sin(phi))
        tb.slab(bm, u(0.0, 0.0, 0.0), u(*spoke), 0.030, 0.016, IRON)

    # Two handles standing off the rim, opposed, like a valve wheel.
    for i in range(2):
        phi = math.radians(30.0 + i * 180.0)
        grip = (radius * 0.78 * math.cos(phi), 0.0, radius * 0.78 * math.sin(phi))
        tb.tube(bm, u(grip[0], 0.0, grip[2]), u(grip[0], 0.07, grip[2]), 0.022, WOOD,
                segments=6)

    return kw.finish(bm, name, PALETTE)


if __name__ == "__main__":
    kw.check_against_blueprint("mine_cart.py")
    kw.write_manifest(
        "Mine", PALETTE, nose_lamps=False, roof_bar=True,
        meshes=[BODY_NAME, WHEEL_FRONT_NAME, WHEEL_REAR_NAME, STEERING_WHEEL_NAME],
    )

    kw.emit(build_body, BODY_NAME, max_tris=3400, max_size_m=3.0)
    kw.emit(lambda: build_wheel(WHEEL_FRONT_NAME, FRONT_WHEEL_RADIUS, kw.FRONT_WHEEL_WIDTH),
            WHEEL_FRONT_NAME, max_tris=900, max_size_m=1.0,
            tread_radius=FRONT_WHEEL_RADIUS)
    kw.emit(lambda: build_wheel(WHEEL_REAR_NAME, REAR_WHEEL_RADIUS, kw.REAR_WHEEL_WIDTH),
            WHEEL_REAR_NAME, max_tris=900, max_size_m=1.0,
            tread_radius=REAR_WHEEL_RADIUS)
    kw.emit(build_steering_wheel, STEERING_WHEEL_NAME, max_tris=520, max_size_m=0.5)
