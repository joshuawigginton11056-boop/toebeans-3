using UnityEngine;

// How an instance is oriented and seated on the ground. Split out of
// TreeScatter so it can be exercised without a scene or an IMGUI event loop
// (see TreeScatterTests).
//
// Trees are placed bolt upright, because real trees grow vertical regardless
// of the slope they stand on. Small props are not: a mushroom or a rock sitting
// perfectly plumb on a 30 degree bank reads as pasted on, and its base wedges
// into the ground on the uphill side. Prop mode therefore leans the instance
// toward the ground normal and seats it along that leaned axis rather than
// along world Y.
internal enum ScatterMode
{
    Trees = 0,
    SmallProps = 1,
}

// The vertical slice of world a prop dart searches for ground. Painting wants
// this kept close to the brush: casting from the top of the world hands back
// whatever roofs the spot - the terrain above a cave, or the volcano's outer
// cone over a passage through it - rather than the surface the cursor is on.
internal struct PropRayWindow
{
    public float startY;
    public float distance;

    // Reaches one span above the brush plane and two below, so a stroke across
    // a steep face still finds ground out at the far edge of the brush.
    public static PropRayWindow AroundBrush(float centerY, float brushRadius)
    {
        float span = Mathf.Max(brushRadius * 2f, 10f);
        return new PropRayWindow { startY = centerY + span, distance = span * 3f };
    }

    // Whole-map scatter has no cursor to anchor to, so take everything. The
    // terrain's own height range doesn't bound the scene - the volcano is a
    // mesh sitting on top of it - hence the headroom above and below.
    public const float Headroom = 500f;

    public static PropRayWindow WholeMap(Vector3 terrainPos, float terrainHeight)
    {
        return new PropRayWindow
        {
            startY = terrainPos.y + terrainHeight + Headroom,
            distance = terrainHeight + Headroom * 2f,
        };
    }

    // Lowest world height the window can return ground from. Only used to
    // reason about coverage in tests.
    public float BottomY => startY - distance;
}

// Deliberately free of Unity's native calls - no Vector3.Slerp, no
// Quaternion.AngleAxis, no Quaternion.Euler. Those throw outside the player, and
// keeping them out is what lets the self-test run this maths headlessly. Cross,
// Dot, Mathf and the Quaternion operators are all managed.
internal static class PropPlacement
{
    // The lean, as a rotation off vertical. Everything else here is derived
    // from it, so the axis and the orientation can't disagree.
    //
    // tilt 0 keeps the prop plumb, tilt 1 lays it flush with the ground, and
    // values between lean it partway - which is what most props want, since
    // fully flush exaggerates every local ripple in the surface.
    public static Quaternion Lean(Vector3 groundNormal, float tilt)
    {
        tilt = Mathf.Clamp01(tilt);
        if (tilt <= 0f || groundNormal.sqrMagnitude < 1e-8f) return Quaternion.identity;

        Vector3 n = groundNormal.normalized;
        Vector3 axis = Vector3.Cross(Vector3.up, n);
        float sin = axis.magnitude;
        float cos = Vector3.Dot(Vector3.up, n);

        if (sin < 1e-6f)
        {
            // Parallel: already plumb, nothing to lean. Anti-parallel: the axis
            // is undefined, so pick one rather than divide by zero - a ground
            // normal pointing straight down isn't placeable anyway, but it must
            // not come back as a NaN that poisons the transform.
            if (cos > 0f) return Quaternion.identity;
            axis = Vector3.right;
            sin = 0f;
        }
        else
        {
            axis /= sin;
        }

        float half = Mathf.Atan2(sin, cos) * tilt * 0.5f;
        float s = Mathf.Sin(half);
        return new Quaternion(axis.x * s, axis.y * s, axis.z * s, Mathf.Cos(half));
    }

    // The axis the instance ends up standing on.
    public static Vector3 UpAxis(Vector3 groundNormal, float tilt)
    {
        return Lean(groundNormal, tilt) * Vector3.up;
    }

    // Yaw is applied about the instance's own up axis, then the whole thing is
    // leaned over. Doing it in the other order would turn the random yaw into a
    // random lean direction, so identical props on the same slope would tip
    // different ways.
    public static Quaternion Rotation(Vector3 groundNormal, float yawDegrees, float tilt)
    {
        float half = yawDegrees * Mathf.Deg2Rad * 0.5f;
        var yaw = new Quaternion(0f, Mathf.Sin(half), 0f, Mathf.Cos(half));
        return Lean(groundNormal, tilt) * yaw;
    }

    // The rotation baked into a prefab's root is part of the model, not a pose
    // the brush is free to replace: a pack whose source art is Z-up carries a
    // -90 degree X rotation there, and that rotation is the only reason the
    // mushroom stands up at all. It goes on innermost, so yaw still spins the
    // prop about its own axis and the lean still comes from the ground alone.
    public static Quaternion Rotation(Vector3 groundNormal, float yawDegrees, float tilt, Quaternion model)
    {
        return Rotation(groundNormal, yawDegrees, tilt) * model;
    }

    // Seats the prefab so its lowest mesh point lands on the surface, then
    // pushes it into the ground by `sink`. Sinking is worth having on a prop
    // brush: rocks and mushrooms bed into the ground in a way tree trunks
    // don't, and it hides the gap left when a flat prefab base meets a surface
    // that isn't flat under it.
    public static Vector3 Position(Vector3 surfacePoint, Vector3 up, float baseOffset, float sink)
    {
        return surfacePoint + up * (baseOffset - sink);
    }
}
