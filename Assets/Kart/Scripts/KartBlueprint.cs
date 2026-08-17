using System.Collections.Generic;
using UnityEngine;

namespace Toebeans.Karting
{
    public enum KartShape { Box, Cylinder, Capsule, Sphere }

    public enum KartSkin { Body, Frame, Rubber, Rim, Seat, Suit, Helmet, Visor }

    /// <summary>Which corner a wheel sits on. The order is fixed: the physics and the model index it the same way.</summary>
    public enum KartCorner { FrontLeft = 0, FrontRight = 1, RearLeft = 2, RearRight = 3 }

    /// <summary>
    /// Which assembly of the kart a part belongs to. A <see cref="KartStyle"/> that ships meshes
    /// swaps whole groups at a time — the chassis and the steering wheel become imported geometry
    /// while the driver and their hands stay primitives, because those are animated. Grouping is
    /// what makes that expressible without matching on part names.
    /// </summary>
    public enum KartPartGroup { Chassis, SteeringWheel, Hands, Driver }

    /// <summary>
    /// One primitive of the kart's proxy model, in the local space of whichever pivot it hangs from.
    /// </summary>
    public struct KartPart
    {
        public string name;
        /// <summary>Empty for the visual root, otherwise the path of a <see cref="KartPivot"/>.</summary>
        public string parent;
        /// <summary>Which assembly this belongs to. Set by <see cref="KartBlueprint.Build"/>.</summary>
        public KartPartGroup group;
        public KartShape shape;
        public KartSkin skin;
        public Vector3 position;
        public Vector3 euler;
        /// <summary>Full size: Box = extents, Sphere = diameter, Cylinder/Capsule = (diameter, length, diameter).</summary>
        public Vector3 size;
    }

    /// <summary>A transform the runtime animates — the steering wheel and the two arm chains hang off these.</summary>
    public struct KartPivot
    {
        public string path;
        public Vector3 position;
        public Vector3 euler;
    }

    /// <summary>
    /// The kart's hard numbers, in metres. This is the single source of truth: the model is built from
    /// it and the wheel anchors are placed from it, so a visual wheel can never drift away from the
    /// wheel that is actually doing the colliding.
    /// </summary>
    public struct KartDimensions
    {
        public float frontAxleZ;
        public float rearAxleZ;
        public float frontTrack;
        public float rearTrack;
        public float frontWheelRadius;
        public float rearWheelRadius;
        public float frontWheelWidth;
        public float rearWheelWidth;

        public static KartDimensions Default => new KartDimensions
        {
            // A shade longer and taller than a racing kart: this one has to cross open terrain, and
            // 5-inch kart wheels would fall into every gap on the mountain.
            frontAxleZ = 0.80f,
            rearAxleZ = -0.85f,
            frontTrack = 1.24f,
            rearTrack = 1.34f,
            frontWheelRadius = 0.26f,
            rearWheelRadius = 0.30f,
            frontWheelWidth = 0.20f,
            rearWheelWidth = 0.28f,
        };

        public float Wheelbase => frontAxleZ - rearAxleZ;

        public bool IsFront(KartCorner corner) =>
            corner == KartCorner.FrontLeft || corner == KartCorner.FrontRight;

        public bool IsLeft(KartCorner corner) =>
            corner == KartCorner.FrontLeft || corner == KartCorner.RearLeft;

        public float Radius(KartCorner corner) => IsFront(corner) ? frontWheelRadius : rearWheelRadius;

        public float Width(KartCorner corner) => IsFront(corner) ? frontWheelWidth : rearWheelWidth;

        /// <summary>Wheel centre in kart local space. Y is the radius, so the tyre just touches y = 0.</summary>
        public Vector3 WheelCentre(KartCorner corner)
        {
            float half = (IsFront(corner) ? frontTrack : rearTrack) * 0.5f;
            return new Vector3(
                IsLeft(corner) ? -half : half,
                Radius(corner),
                IsFront(corner) ? frontAxleZ : rearAxleZ);
        }

        public static readonly KartCorner[] Corners =
        {
            KartCorner.FrontLeft, KartCorner.FrontRight, KartCorner.RearLeft, KartCorner.RearRight
        };
    }

    /// <summary>
    /// Builds the kart and its driver out of primitives, at real-world size, with the driver posed
    /// sitting in the seat. Deliberately free of scene objects, asset loading and Unity's native calls
    /// so the whole layout can be executed and asserted outside the Editor.
    /// </summary>
    public static class KartBlueprint
    {
        // ------------------------------------------------------------------ driver reference points
        // A 1.8 m adult folded into the seat. Everything below is measured off these, so nudging the
        // seat moves the whole driver rather than leaving them hovering over it.

        public const float DriverHeightStanding = 1.8f;

        public static readonly Vector3 SeatBaseTop = new Vector3(0f, 0.37f, -0.42f);
        public static readonly Vector3 Hip = new Vector3(0.12f, 0.47f, -0.34f);
        public static readonly Vector3 Knee = new Vector3(0.16f, 0.55f, 0.10f);
        // Heels rest on top of the floor pan, not through it. The pan's upper face is at y = 0.23.
        public static readonly Vector3 Ankle = new Vector3(0.17f, 0.32f, 0.47f);
        public static readonly Vector3 Shoulder = new Vector3(0.21f, 0.97f, -0.50f);
        public static readonly Vector3 HelmetCentre = new Vector3(0f, 1.18f, -0.55f);

        public const float HelmetRadius = 0.125f;

        /// <summary>Where the steering wheel's hub sits, and the axis its column runs along.</summary>
        public static readonly Vector3 SteeringHub = new Vector3(0f, 0.76f, -0.02f);

        public static readonly Vector3 SteeringRack = new Vector3(0f, 0.30f, 0.30f);

        public const float SteeringWheelRadius = 0.16f;
        public const int SteeringRimSegments = 10;

        public const float UpperArmLength = 0.33f;
        public const float ForearmLength = 0.28f;

        // ------------------------------------------------------------------ chassis reference points

        public const float RollHoopTopY = 1.40f;
        public const float RollHoopZ = -0.72f;
        public const float RollHoopHalfWidth = 0.40f;

        public const string SteeringPivotPath = "Steering";
        public const string DriverPivotPath = "Driver";

        public static IReadOnlyList<KartPivot> Pivots(KartDimensions d)
        {
            return new[]
            {
                // Local +Y runs up the steering column, so spinning this pivot about its own Y turns
                // the wheel in its proper plane — and the hands, which hang off it, come along.
                new KartPivot
                {
                    path = SteeringPivotPath,
                    position = SteeringHub,
                    euler = new Vector3(TiltInYZPlane(SteeringHub - SteeringRack), 0f, 0f),
                },
                new KartPivot { path = DriverPivotPath, position = Vector3.zero, euler = Vector3.zero },
            };
        }

        public static List<KartPart> Build(KartDimensions d, bool includeDriver = true)
        {
            var parts = new List<KartPart>();

            // The steering column is grouped with the chassis, not with the wheel: it does not turn,
            // so a style that replaces the chassis with a mesh has to take the column with it.
            int start = parts.Count;
            AddChassis(parts, d);
            AddSteeringColumn(parts);
            StampGroup(parts, start, KartPartGroup.Chassis);

            start = parts.Count;
            AddSteeringWheel(parts);
            StampGroup(parts, start, KartPartGroup.SteeringWheel);

            start = parts.Count;
            AddHands(parts);
            StampGroup(parts, start, KartPartGroup.Hands);

            if (includeDriver)
            {
                start = parts.Count;
                AddDriver(parts);
                StampGroup(parts, start, KartPartGroup.Driver);
            }

            return parts;
        }

        /// <summary>KartPart is a struct, so a group set on a copy has to be written back to the list.</summary>
        static void StampGroup(List<KartPart> parts, int from, KartPartGroup group)
        {
            for (int i = from; i < parts.Count; i++)
            {
                KartPart part = parts[i];
                part.group = group;
                parts[i] = part;
            }
        }

        /// <summary>
        /// The four wheel visuals, built in the local space of their own corner pivot. These carry no
        /// group — a mesh style replaces the lot by not calling this at all.
        /// </summary>
        public static List<KartPart> BuildWheel(KartDimensions d, KartCorner corner)
        {
            float radius = d.Radius(corner);
            float width = d.Width(corner);

            return new List<KartPart>
            {
                // The wheel pivot's local Y is up; the axle runs along local X.
                Cyl("Tyre", Vector3.zero, new Vector3(0f, 0f, 90f), radius * 2f, width, KartSkin.Rubber),
                Cyl("Rim", Vector3.zero, new Vector3(0f, 0f, 90f), radius * 1.15f, width * 1.02f, KartSkin.Rim),
                // A single lug pokes out of the rim so the wheel visibly spins rather than just blurring.
                Box("Lug", new Vector3(0f, radius * 0.42f, 0f), Vector3.zero,
                    new Vector3(width * 1.1f, radius * 0.18f, radius * 0.18f), KartSkin.Frame),
            };
        }

        // ------------------------------------------------------------------ chassis

        static void AddChassis(List<KartPart> parts, KartDimensions d)
        {
            // Floor pan and the two frame rails it sits between.
            parts.Add(Box("FloorPan", new Vector3(0f, 0.20f, -0.05f), Vector3.zero,
                new Vector3(0.66f, 0.06f, 1.95f), KartSkin.Frame));

            for (int s = -1; s <= 1; s += 2)
            {
                string side = s < 0 ? "L" : "R";
                parts.Add(Segment($"Rail{side}",
                    new Vector3(0.36f * s, 0.26f, -1.00f), new Vector3(0.36f * s, 0.26f, 0.95f),
                    0.08f, KartSkin.Frame));

                // Side pods, kept inboard of the rear tyres so nothing intersects at full lock, and
                // level with the floor pan's underside so the pan stays the lowest thing on the kart.
                parts.Add(Box($"SidePod{side}", new Vector3(0.46f * s, 0.27f, -0.05f), Vector3.zero,
                    new Vector3(0.26f, 0.20f, 0.86f), KartSkin.Body));

                // Roll hoop: upright plus a brace running back to the bumper.
                parts.Add(Segment($"RollHoop{side}",
                    new Vector3(RollHoopHalfWidth * s, 0.25f, RollHoopZ),
                    new Vector3(RollHoopHalfWidth * s, RollHoopTopY, RollHoopZ),
                    0.07f, KartSkin.Frame));
                parts.Add(Segment($"RollBrace{side}",
                    new Vector3(RollHoopHalfWidth * s, RollHoopTopY - 0.02f, RollHoopZ - 0.03f),
                    new Vector3(RollHoopHalfWidth * s, 0.35f, -1.12f),
                    0.06f, KartSkin.Frame));

                // Front wishbone stubs reaching out to the hubs.
                parts.Add(Segment($"FrontArm{side}",
                    new Vector3(0.30f * s, d.frontWheelRadius, d.frontAxleZ),
                    new Vector3((d.frontTrack * 0.5f - 0.06f) * s, d.frontWheelRadius, d.frontAxleZ),
                    0.06f, KartSkin.Frame));
            }

            parts.Add(Segment("RollHoopTop",
                new Vector3(-RollHoopHalfWidth, RollHoopTopY, RollHoopZ),
                new Vector3(RollHoopHalfWidth, RollHoopTopY, RollHoopZ),
                0.07f, KartSkin.Frame));

            // Live rear axle, at exactly rear wheel centre height.
            parts.Add(Segment("RearAxle",
                new Vector3(-d.rearTrack * 0.5f, d.rearWheelRadius, d.rearAxleZ),
                new Vector3(d.rearTrack * 0.5f, d.rearWheelRadius, d.rearAxleZ),
                0.08f, KartSkin.Frame));

            // Nose, sloped so the kart reads as pointing somewhere.
            // Sloped, and high enough that its leading lower edge still clears the floor pan.
            parts.Add(Box("NoseCone", new Vector3(0f, 0.30f, 1.02f), new Vector3(-12f, 0f, 0f),
                new Vector3(0.62f, 0.16f, 0.42f), KartSkin.Body));
            parts.Add(Segment("FrontBumper",
                new Vector3(-0.51f, 0.30f, 1.24f), new Vector3(0.51f, 0.30f, 1.24f),
                0.07f, KartSkin.Frame));
            parts.Add(Segment("RearBumper",
                new Vector3(-0.65f, 0.30f, -1.16f), new Vector3(0.65f, 0.30f, -1.16f),
                0.08f, KartSkin.Frame));

            // Seat.
            parts.Add(Box("SeatBase", new Vector3(0f, 0.32f, -0.42f), Vector3.zero,
                new Vector3(0.50f, 0.10f, 0.50f), KartSkin.Seat));
            parts.Add(Box("SeatBack", new Vector3(0f, 0.66f, -0.68f), new Vector3(-18f, 0f, 0f),
                new Vector3(0.50f, 0.62f, 0.10f), KartSkin.Seat));
            parts.Add(Box("SeatBolsterL", new Vector3(-0.27f, 0.46f, -0.48f), Vector3.zero,
                new Vector3(0.06f, 0.28f, 0.46f), KartSkin.Seat));
            parts.Add(Box("SeatBolsterR", new Vector3(0.27f, 0.46f, -0.48f), Vector3.zero,
                new Vector3(0.06f, 0.28f, 0.46f), KartSkin.Seat));

            // Engine sits behind the seat, inboard of the rear tyres.
            parts.Add(Box("Engine", new Vector3(0f, 0.56f, -1.00f), Vector3.zero,
                new Vector3(0.42f, 0.38f, 0.34f), KartSkin.Body));
            parts.Add(Segment("Exhaust",
                new Vector3(0.14f, 0.70f, -1.02f), new Vector3(0.14f, 1.02f, -1.16f),
                0.06f, KartSkin.Rim));

            // Pedals, so the driver's feet land on something. Hung above the floor pan rather than
            // below it — anything that reaches lower than the pan is the first thing to ground out.
            parts.Add(Box("PedalL", new Vector3(-0.17f, 0.30f, 0.66f), new Vector3(-20f, 0f, 0f),
                new Vector3(0.10f, 0.14f, 0.03f), KartSkin.Rim));
            parts.Add(Box("PedalR", new Vector3(0.17f, 0.30f, 0.66f), new Vector3(-20f, 0f, 0f),
                new Vector3(0.10f, 0.14f, 0.03f), KartSkin.Rim));
        }

        // ------------------------------------------------------------------ steering

        static void AddSteeringColumn(List<KartPart> parts)
        {
            parts.Add(Segment("SteeringColumn", SteeringRack, SteeringHub, 0.045f, KartSkin.Frame));
        }

        static void AddSteeringWheel(List<KartPart> parts)
        {
            // Rim built from chords rather than one disc, so it reads as a wheel and not a dinner plate.
            float chord = 2f * SteeringWheelRadius * Mathf.Sin(Mathf.PI / SteeringRimSegments) * 1.08f;
            for (int i = 0; i < SteeringRimSegments; i++)
            {
                float phi = i * 2f * Mathf.PI / SteeringRimSegments;
                var position = new Vector3(
                    SteeringWheelRadius * Mathf.Cos(phi), 0f, SteeringWheelRadius * Mathf.Sin(phi));
                // Turn the segment's long (local X) axis onto the circle's tangent at phi.
                float yaw = -(phi * Mathf.Rad2Deg + 90f);
                parts.Add(Box($"Rim{i}", position, new Vector3(0f, yaw, 0f),
                    new Vector3(chord, 0.035f, 0.035f), KartSkin.Rubber, SteeringPivotPath));
            }

            parts.Add(Cyl("Hub", Vector3.zero, Vector3.zero, 0.10f, 0.05f, KartSkin.Rim, SteeringPivotPath));
            parts.Add(Box("Spoke0", Vector3.zero, Vector3.zero,
                new Vector3(SteeringWheelRadius * 2f, 0.025f, 0.05f), KartSkin.Rim, SteeringPivotPath));
            parts.Add(Box("Spoke1", Vector3.zero, new Vector3(0f, 90f, 0f),
                new Vector3(SteeringWheelRadius, 0.025f, 0.05f), KartSkin.Rim, SteeringPivotPath));
        }

        /// <summary>
        /// Hands hang off the steering pivot, so they orbit with the rim for free. Their own group,
        /// because a mesh style replaces the rim but still needs somewhere for the arms to reach —
        /// KartDriverRig finds these by name and aims the forearms at them.
        /// </summary>
        static void AddHands(List<KartPart> parts)
        {
            parts.Add(Sphere("HandL", new Vector3(-SteeringWheelRadius, 0f, 0f), 0.10f,
                KartSkin.Suit, SteeringPivotPath));
            parts.Add(Sphere("HandR", new Vector3(SteeringWheelRadius, 0f, 0f), 0.10f,
                KartSkin.Suit, SteeringPivotPath));
        }

        // ------------------------------------------------------------------ driver

        static void AddDriver(List<KartPart> parts)
        {
            parts.Add(Box("Pelvis", new Vector3(0f, 0.45f, -0.38f), Vector3.zero,
                new Vector3(0.34f, 0.22f, 0.28f), KartSkin.Suit, DriverPivotPath));

            for (int s = -1; s <= 1; s += 2)
            {
                string side = s < 0 ? "L" : "R";
                Vector3 hip = Mirror(Hip, s);
                Vector3 knee = Mirror(Knee, s);
                Vector3 ankle = Mirror(Ankle, s);
                Vector3 shoulder = Mirror(Shoulder, s);

                parts.Add(Segment($"Thigh{side}", hip, knee, 0.17f, KartSkin.Suit,
                    DriverPivotPath, KartShape.Capsule));
                parts.Add(Segment($"Shin{side}", knee, ankle, 0.13f, KartSkin.Suit,
                    DriverPivotPath, KartShape.Capsule));
                parts.Add(Box($"Foot{side}", ankle + new Vector3(0f, -0.04f, 0.10f), new Vector3(-8f, 0f, 0f),
                    new Vector3(0.11f, 0.07f, 0.26f), KartSkin.Seat, DriverPivotPath));

                // Arms are placed in a plausible rest pose here and re-aimed at the wheel every frame
                // by KartDriverRig, which is what makes the driver look like they are steering.
                parts.Add(Segment($"UpperArm{side}", shoulder, shoulder + new Vector3(0.05f * s, -0.14f, 0.30f),
                    0.11f, KartSkin.Suit, DriverPivotPath, KartShape.Capsule));
                parts.Add(Segment($"Forearm{side}", shoulder + new Vector3(0.05f * s, -0.14f, 0.30f),
                    new Vector3(SteeringWheelRadius * s, SteeringHub.y, SteeringHub.z),
                    0.09f, KartSkin.Suit, DriverPivotPath, KartShape.Capsule));
            }

            parts.Add(Segment("Torso", new Vector3(0f, 0.50f, -0.40f), new Vector3(0f, 1.00f, -0.52f),
                0.34f, KartSkin.Suit, DriverPivotPath, KartShape.Capsule));
            parts.Add(Segment("Neck", new Vector3(0f, 0.99f, -0.51f), new Vector3(0f, 1.07f, -0.53f),
                0.10f, KartSkin.Suit, DriverPivotPath, KartShape.Capsule));

            parts.Add(Sphere("Helmet", HelmetCentre, HelmetRadius * 2f, KartSkin.Helmet, DriverPivotPath));
            parts.Add(Box("Visor", HelmetCentre + new Vector3(0f, -0.01f, 0.10f), new Vector3(-6f, 0f, 0f),
                new Vector3(0.17f, 0.08f, 0.08f), KartSkin.Visor, DriverPivotPath));
        }

        static Vector3 Mirror(Vector3 v, int side) => new Vector3(v.x * side, v.y, v.z);

        // ------------------------------------------------------------------ primitive helpers

        static KartPart Box(string name, Vector3 position, Vector3 euler, Vector3 size, KartSkin skin,
            string parent = "")
        {
            return new KartPart
            {
                name = name, parent = parent, shape = KartShape.Box, skin = skin,
                position = position, euler = euler, size = size,
            };
        }

        static KartPart Sphere(string name, Vector3 position, float diameter, KartSkin skin,
            string parent = "")
        {
            return new KartPart
            {
                name = name, parent = parent, shape = KartShape.Sphere, skin = skin,
                position = position, euler = Vector3.zero, size = new Vector3(diameter, diameter, diameter),
            };
        }

        static KartPart Cyl(string name, Vector3 position, Vector3 euler, float diameter, float length,
            KartSkin skin, string parent = "")
        {
            return new KartPart
            {
                name = name, parent = parent, shape = KartShape.Cylinder, skin = skin,
                position = position, euler = euler, size = new Vector3(diameter, length, diameter),
            };
        }

        /// <summary>
        /// A cylinder or capsule spanning two points. Working in endpoints rather than
        /// centre-plus-rotation is what keeps the frame joined up when a dimension changes.
        /// </summary>
        static KartPart Segment(string name, Vector3 from, Vector3 to, float diameter, KartSkin skin,
            string parent = "", KartShape shape = KartShape.Cylinder)
        {
            Vector3 delta = to - from;
            float length = delta.magnitude;
            return new KartPart
            {
                name = name,
                parent = parent,
                shape = shape,
                skin = skin,
                position = (from + to) * 0.5f,
                euler = EulerAligningUp(delta),
                size = new Vector3(diameter, Mathf.Max(length, 0.001f), diameter),
            };
        }

        /// <summary>
        /// Euler angles whose local +Y points along <paramref name="direction"/> — the axis every
        /// cylinder and capsule primitive runs along.
        /// </summary>
        public static Vector3 EulerAligningUp(Vector3 direction)
        {
            Vector3 dir = direction.normalized;
            if (dir.sqrMagnitude < 1e-8f)
                return Vector3.zero;

            // Unity applies Euler angles Z, then X, then Y, so local up lands on
            // (sin(yaw)sin(pitch), cos(pitch), cos(yaw)sin(pitch)).
            // For anything in the YZ plane a signed pitch alone does it, and keeping yaw at zero there
            // means local X still points along world X — which is what lets the steering pivot's
            // left-hand side stay on the kart's left.
            if (Mathf.Abs(dir.x) < 1e-4f)
                return new Vector3(TiltInYZPlane(dir), 0f, 0f);

            float pitch = Mathf.Acos(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            return new Vector3(pitch, yaw, 0f);
        }

        static float TiltInYZPlane(Vector3 direction) =>
            Mathf.Atan2(direction.z, direction.y) * Mathf.Rad2Deg;

        /// <summary>
        /// Converts a part's <see cref="KartPart.size"/> into the localScale its primitive needs.
        /// Unity's cylinder and capsule are two units tall, which is the single most common way a
        /// hand-built rig ends up double the intended length.
        /// </summary>
        public static Vector3 LocalScale(KartPart part)
        {
            switch (part.shape)
            {
                case KartShape.Cylinder:
                case KartShape.Capsule:
                    return new Vector3(part.size.x, part.size.y * 0.5f, part.size.z);
                default:
                    return part.size;
            }
        }
    }
}
