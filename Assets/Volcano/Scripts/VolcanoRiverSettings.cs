using UnityEngine;

namespace Volcano
{
    /// <summary>
    /// Everything about the rivers that pour out of the spillway notches, on the volcano rather than
    /// buried in the editor script that builds them.
    ///
    /// These used to be constants inside the dressing code, which meant the only way to change how
    /// far a river ran or how fast it looked was to edit C#. They are settings now because the route
    /// past the foot of the mountain is a level-design decision, not a property of the mountain.
    ///
    /// Nothing here shapes the cone. The volcano mesh ignores this object completely; it is read by
    /// the "Add Spillway Rivers" button and written onto the Lava Flow generators it makes.
    /// </summary>
    [System.Serializable]
    public class VolcanoRiverSettings
    {
        // ---------------------------------------------------------------- route

        [Header("Route")]
        [Tooltip("How far a river keeps running after it leaves the foot of the mountain, in metres. " +
                 "The stretch on the cone itself is fixed by the spillway channel; this is the part " +
                 "out on the map, so this is the knob for taking lava further around the island.\n\n" +
                 "0 stops the river at the skirt.")]
        [Range(0f, 600f)] public float runOutLength = 140f;

        [Tooltip("Distance between the route points generated out on the flat, in metres. These are " +
                 "the waypoints you can then drag; fewer, further apart is easier to author with.")]
        [Range(4f, 40f)] public float runOutStep = 12f;

        [Tooltip("How far the run-out swings off the radial line, in metres. A channel down a cone " +
                 "leaves on a dead straight bearing and out on the flat that reads as a canal.")]
        [Range(0f, 40f)] public float meanderAmplitude = 9f;

        [Tooltip("Distance for one full swing of that meander, in metres.")]
        [Range(20f, 400f)] public float meanderLength = 140f;

        // ---------------------------------------------------------------- channel

        [Header("Channel")]
        [Tooltip("Width of the river where it is cascading down the flank, in metres.")]
        [Range(0.5f, 30f)] public float cascadeWidth = 6f;

        [Tooltip("Width once it reaches the flat, in metres.")]
        [Range(1f, 80f)] public float riverWidth = 14f;

        [Tooltip("Distance between cross-sections, in metres. Smaller hugs the ground more closely " +
                 "and costs proportionally more triangles.")]
        [Range(0.6f, 6f)] public float stationSpacing = 2.2f;

        [Tooltip("How closely the surface follows the ground under it.\n\n" +
                 "The mountain is built out of large flat facets, and at 1 the lava creases over " +
                 "every one of them. Backing it off is most of what makes a river read as a smooth " +
                 "pour rather than a crumpled sheet.")]
        [Range(0f, 1f)] public float groundFollow = 0.7f;

        // ---------------------------------------------------------------- look

        [Header("Look")]
        [Tooltip("How much the lava surface rolls, buckles and varies in width.\n\n" +
                 "This is one knob over the flow's turbulence, its pressure ridges, its plate " +
                 "height variation, its bank roughness and its width variation. Low is a smooth " +
                 "molten pour; high is the churned, broken surface of a slow-moving field.")]
        [Range(0f, 1f)] public float surfaceRipple = 0.22f;

        [Tooltip("How fast the lava appears to pour, in metres per second. Written onto the river " +
                 "material, so it is the speed you actually see rather than anything the geometry " +
                 "does. Real lava crawls; anything much above 1 reads as water.")]
        [Range(0f, 6f)] public float flowSpeed = 0.55f;

        [Tooltip("How much faster the cascade down the flank looks than the river out on the flat.")]
        [Range(1f, 8f)] public float cascadeSpeedBoost = 1.8f;

        [Tooltip("Size of the pattern in the molten surface. Larger numbers mean smaller, busier " +
                 "detail; small numbers give the broad slow swirls of a thick melt.\n\n" +
                 "Going too low is its own kind of ugly: the dark cooling patches grow with " +
                 "everything else and stop reading as skin on lava, turning into smudges.")]
        [Range(0.1f, 12f)] public float patternScale = 1.2f;

        [Tooltip("How much of the molten surface has a dark cooling skin over it. This is drawn by " +
                 "the material and is separate from the crust plates, which are geometry.\n\n" +
                 "It is the other half of 'smooth': a hot open pour wants very little of it, and it " +
                 "is the first thing to turn down if the lava looks blotchy rather than molten. The " +
                 "crust plates are geometry and carry the cooled-over story on their own, so this " +
                 "can go very low without the river losing anything.")]
        [Range(0f, 1f)] public float moltenCrust = 0.1f;

        [Tooltip("How much the pattern is swirled as it travels. This is the other half of 'ripply': " +
                 "a strong swirl on a slow flow reads as the surface churning in place.")]
        [Range(0f, 3f)] public float swirl = 0.3f;

        [Tooltip("How much the pattern is drawn out along the direction of travel. Lava that is " +
                 "moving gets stretched into ropes pointing the way it is going, and that stretch is " +
                 "the main cue that tells the eye which way a river is flowing.")]
        [Range(0.2f, 6f)] public float stretchAlongFlow = 3.4f;

        // ---------------------------------------------------------------- blocking

        [Header("Blocking")]
        [Tooltip("Put an invisible wall down each bank so a kart cannot drive into the lava or " +
                 "across it.\n\n" +
                 "The flow mesh itself is no use as a collider: it stands less than a metre off the " +
                 "ground, so a kart drives straight onto it and over the far side. This builds a " +
                 "separate barrier that is tall enough to stop one.")]
        public bool blockKarts = true;

        [Tooltip("How high the barrier stands above the bank, in metres. It wants to be well over " +
                 "the height a kart can climb, and it is invisible, so err high.")]
        [Range(0.5f, 12f)] public float barrierHeight = 3.5f;

        [Tooltip("How far the barrier is pulled in from the outer edge of the flow, in metres. A " +
                 "little inset lets a kart clip the buried skirt, which is dressing, without " +
                 "reaching the channel.")]
        [Range(0f, 6f)] public float barrierInset = 0.6f;

        [Tooltip("How far the barrier is sunk below the ground it stands on, in metres. Keeps it " +
                 "sealed where the ground under the two banks is at different heights.")]
        [Range(0f, 6f)] public float barrierSink = 1.5f;

        public VolcanoRiverSettings Clone()
        {
            return (VolcanoRiverSettings)MemberwiseClone();
        }
    }
}
