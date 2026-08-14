using System.Collections.Generic;
using UnityEngine;

namespace LavaFlow
{
    /// <summary>Where the centreline of the flow comes from.</summary>
    public enum FlowPathMode
    {
        /// <summary>Released at the generator's position and left to run downhill over the ground,
        /// the way real lava picks its route. Nothing to author but the start point.</summary>
        Downhill = 0,

        /// <summary>Follows points dragged in the scene view. Use this to trace an exact route.</summary>
        Waypoints = 1,

        /// <summary>Follows a Spline Container, for anyone who would rather author with splines.</summary>
        Spline = 2
    }

    /// <summary>How the generated UVs are laid out.</summary>
    public enum FlowUVMode
    {
        /// <summary>U across the channel in 0-1, V in metres down it. The only mode a scrolling
        /// lava material can use, because it is the only one that knows which way is downstream.</summary>
        FlowAligned = 0,

        /// <summary>One 0-1 tile stretched over the whole flow, for shaders that mask on UV.</summary>
        Normalized = 1,

        /// <summary>Projected from world position, tiling every uvScale metres.</summary>
        WorldPlanar = 2
    }

    /// <summary>
    /// Every knob that shapes the lava flow. A plain serializable class, so the same settings can be
    /// authored in the inspector or handed to <see cref="LavaFlowMeshBuilder"/> from a test.
    /// </summary>
    [System.Serializable]
    public class LavaFlowSettings
    {
        [Header("Seed")]
        [Tooltip("Change this for a different flow along the same route.")]
        public int seed = 20260803;

        // ---------------------------------------------------------------- path

        [Header("Path")]
        [Tooltip("Where the centreline comes from.\n\n" +
                 "Downhill releases lava at this object and lets it run down the terrain.\n\n" +
                 "Waypoints follows the points you drag in the scene view.\n\n" +
                 "Spline follows a Spline Container.")]
        public FlowPathMode pathMode = FlowPathMode.Downhill;

        [Tooltip("Waypoints mode only. Positions are local to this object; drag them in the scene view.")]
        public List<Vector3> waypoints = new List<Vector3>();

        [Tooltip("Downhill mode only. How far lava released at the source is allowed to run before " +
                 "it stops. Waypoint and spline routes are exactly as long as you drew them and " +
                 "ignore this.")]
        [Range(10f, 3000f)] public float maxLength = 220f;

        [Tooltip("Distance between cross-sections. Smaller hugs the ground more closely and costs " +
                 "proportionally more triangles.")]
        [Range(0.4f, 6f)] public float stationSpacing = 1.6f;

        [Tooltip("Downhill mode. How much the flow keeps its existing heading rather than turning " +
                 "straight down the slope. Lava has momentum: at 0 it snaps into every little gully, " +
                 "at 1 it ignores the terrain completely.")]
        [Range(0f, 0.95f)] public float momentum = 0.55f;

        [Tooltip("Downhill mode. Sideways wander, so the route is not a perfect fall line.")]
        [Range(0f, 1f)] public float wander = 0.25f;

        [Tooltip("Downhill mode. Below this ground slope the flow counts as having reached the flat, " +
                 "and it spends the rest of its length spreading out as the river.")]
        [Range(1f, 30f)] public float flatSlopeAngle = 8f;

        [Tooltip("Downhill mode. How far the river keeps running after the ground goes flat, in " +
                 "metres. This is the stretch at the bottom, so give it room.")]
        [Range(0f, 400f)] public float riverRunLength = 80f;

        [Tooltip("How closely the surface follows the ground. 0 leaves the flow rigid and floating; " +
                 "1 makes it cling to every bump, including ones too small for lava to notice.")]
        [Range(0f, 1f)] public float groundFollow = 0.85f;

        [Tooltip("How far the flow sits above the ground it was draped onto, in metres. A little " +
                 "lift stops it z-fighting with the terrain.")]
        [Range(0f, 2f)] public float surfaceOffset = 0.12f;

        // ---------------------------------------------------------------- channel

        [Header("Channel")]
        [Tooltip("Width of the flow where it is running fast down the steep ground, in metres. " +
                 "Lava on a slope is narrow and quick.")]
        [Range(0.5f, 30f)] public float cascadeWidth = 4.5f;

        [Tooltip("Width once the flow reaches the flat and becomes the river, in metres. Slow lava " +
                 "spreads.")]
        [Range(1f, 80f)] public float riverWidth = 14f;

        [Tooltip("Ground slope, in degrees, at which the flow is considered fully in cascade. " +
                 "Everything steeper is narrow, fast and mostly molten.")]
        [Range(5f, 80f)] public float steepAngle = 34f;

        [Tooltip("How much the width breathes along the route.")]
        [Range(0f, 0.8f)] public float widthVariation = 0.28f;

        [Tooltip("Cross-sections across the channel. Drives the poly budget together with spacing.")]
        [Range(4, 40)] public int lateralSegments = 14;

        [Tooltip("How far the lava surface sits below the top of its banks, in metres.")]
        [Range(0f, 4f)] public float channelDepth = 0.55f;

        [Tooltip("Downhill mode only. Sideways swing of the channel on the flat, in metres. Rivers " +
                 "meander; a dead straight one reads as a canal. A route you drew yourself is left " +
                 "exactly where you drew it, so this does nothing there.")]
        [Range(0f, 20f)] public float meander = 4.5f;

        [Tooltip("Wavelength of that meander, in metres.")]
        [Range(5f, 200f)] public float meanderLength = 55f;

        // ---------------------------------------------------------------- levees

        [Header("Levees")]
        [Tooltip("Fraction of the half-width taken up by the cooled bank on each side. The channel " +
                 "gets what is left.")]
        [Range(0.05f, 0.6f)] public float leveeFraction = 0.26f;

        [Tooltip("How high the banks stand above the ground, in metres. Lava builds its own walls " +
                 "out of what cools at the edges, and they are what sells a flow as self-made " +
                 "rather than painted on.")]
        [Range(0f, 4f)] public float leveeHeight = 0.75f;

        [Tooltip("How ragged the banks are.")]
        [Range(0f, 1f)] public float leveeRoughness = 0.6f;

        [Tooltip("How far the outer edge is buried into the ground, in metres. Keep this above the " +
                 "size of the bumps in your terrain or the flow will show daylight under its edge.")]
        [Range(0f, 5f)] public float skirtDepth = 1.2f;

        // ---------------------------------------------------------------- crust

        [Header("Crust")]
        [Tooltip("How much of the river's surface has crusted over. The gaps between the plates are " +
                 "where the lava shows through.")]
        [Range(0f, 1f)] public float crustCoverageRiver = 0.82f;

        [Tooltip("How much of the cascade's surface has crusted over. Lava moving fast down a steep " +
                 "face barely skins over at all, which is why the cascades read as the bright part.")]
        [Range(0f, 1f)] public float crustCoverageCascade = 0.12f;

        [Tooltip("Fraction of the surviving plates that are still glowing rather than fully cooled.")]
        [Range(0f, 1f)] public float warmCrustRatio = 0.33f;

        [Tooltip("Length of a crust plate, in metres. Plates are stretched along the flow because " +
                 "that is the direction they are being dragged in.")]
        [Range(0.5f, 20f)] public float plateLength = 4.5f;

        [Tooltip("Width of a crust plate, in metres.")]
        [Range(0.3f, 12f)] public float plateWidth = 2.2f;

        [Tooltip("How irregular the plate boundaries are. 0 gives a brick wall.")]
        [Range(0f, 1f)] public float plateJitter = 0.75f;

        [Tooltip("How far the plates pull back from each other, in metres. This is the width of the " +
                 "glowing cracks and the single most visible knob here.")]
        [Range(0f, 1.5f)] public float crackWidth = 0.2f;

        [Tooltip("Thickness of the crust. Reads as the depth of the plates when you look into a crack.")]
        [Range(0.02f, 2f)] public float crustThickness = 0.22f;

        [Tooltip("Vertical offset between neighbouring plates, so the crust is not a flat lid.")]
        [Range(0f, 0.8f)] public float plateHeightVariation = 0.12f;

        // ---------------------------------------------------------------- ridges

        [Header("Pressure ridges")]
        [Tooltip("Height of the arcs of buckled crust that form where the flow is being held back, " +
                 "in metres. They bow downstream because the middle of the channel outruns the edges.")]
        [Range(0f, 2f)] public float ridgeHeight = 0.35f;

        [Tooltip("Distance between ridges, in metres.")]
        [Range(2f, 60f)] public float ridgeSpacing = 11f;

        [Tooltip("How far the arcs bow. 0 gives straight bars across the channel, which never happens.")]
        [Range(0f, 3f)] public float ridgeCurvature = 1.2f;

        // ---------------------------------------------------------------- molten

        [Header("Molten")]
        [Tooltip("How much the lava surface rolls and bulges.")]
        [Range(0f, 1f)] public float moltenTurbulence = 0.5f;

        // ---------------------------------------------------------------- detail

        [Header("Detail")]
        [Tooltip("Slabs of crust tipped up out of the surface where plates have jammed.")]
        [Range(0, 120)] public int slabCount = 26;

        [Range(0.3f, 5f)] public float slabSize = 1.3f;
        [Range(0f, 3f)] public float slabHeight = 0.8f;

        [Tooltip("Domes swelling up out of the open lava. Only placed on the slow stretches; " +
                 "nothing has time to bubble on a cascade.")]
        [Range(0, 120)] public int bubbleCount = 20;

        [Range(0.1f, 4f)] public float bubbleSize = 0.8f;

        [Tooltip("Boulders rafted along on the banks.")]
        [Range(0, 160)] public int rockCount = 34;

        [Range(0.1f, 4f)] public float rockSize = 0.85f;

        // ---------------------------------------------------------------- output

        [Header("Output")]
        [Tooltip("How the UVs are laid out.\n\n" +
                 "Flow Aligned runs V down the channel in metres and is what the scrolling lava " +
                 "material wants.\n\n" +
                 "Normalised stretches one 0-1 tile over everything, for shaders that mask or remap " +
                 "on UV rather than tiling.\n\n" +
                 "World Planar tiles every uvScale metres, for ordinary rock textures.")]
        public FlowUVMode uvMode = FlowUVMode.FlowAligned;

        [Tooltip("Metres per UV tile.")]
        [Range(0.1f, 40f)] public float uvScale = 6f;

        [Tooltip("How much faster the surface appears to move on the cascades than on the river. " +
                 "Written into UV1 for the material; geometry ignores it.")]
        [Range(1f, 8f)] public float cascadeSpeedBoost = 3.2f;

        public LavaFlowSettings Clone()
        {
            var copy = (LavaFlowSettings)MemberwiseClone();
            copy.waypoints = new List<Vector3>(waypoints);
            return copy;
        }
    }
}
