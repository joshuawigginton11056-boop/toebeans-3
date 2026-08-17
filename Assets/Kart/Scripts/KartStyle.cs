using System.Collections.Generic;

namespace Toebeans.Karting
{
    /// <summary>
    /// How a kart looks, as opposed to how it drives. A style names the meshes to hang on the rig
    /// that <see cref="KartBlueprint"/> lays out; it says nothing about dimensions, because those
    /// come from <see cref="KartDimensions"/> and are the same for every style. Swapping styles
    /// changes the bodywork and leaves the physics, the wheel anchors and the driver alone.
    ///
    /// A style with no meshes falls back to the primitive kart, which is still the reference
    /// implementation: it needs no imported assets, so it always builds, and it is what a new style
    /// is checked against when its meshes look wrong.
    ///
    /// Adding a style is adding an entry to <see cref="All"/> and building its meshes with
    /// Tools/blender. Nothing else here has to change.
    /// </summary>
    public sealed class KartStyle
    {
        /// <summary>Shown in the menu and logged when the kart is built.</summary>
        public string name;

        /// <summary>
        /// Mesh asset names under Assets/GeneratedModels, without the .fbx. Leave the body null to
        /// build that style out of primitives instead.
        /// </summary>
        public string bodyMesh;
        public string wheelFrontMesh;
        public string wheelRearMesh;

        /// <summary>
        /// Optional. When null the primitive rim is used even by a mesh style, so a work-in-progress
        /// style still gets something to hold on to and the driver's hands still land on a wheel.
        /// </summary>
        public string steeringWheelMesh;

        public bool UsesMeshes => !string.IsNullOrEmpty(bodyMesh);

        public bool UsesMeshSteeringWheel => !string.IsNullOrEmpty(steeringWheelMesh);

        /// <summary>
        /// The mesh for one corner. Front and rear are separate assets because they are separate
        /// sizes in KartDimensions, and a rear tyre stretched onto a front hub reads as wrong long
        /// before anyone works out why.
        /// </summary>
        public string WheelMesh(KartCorner corner) =>
            corner == KartCorner.FrontLeft || corner == KartCorner.FrontRight
                ? wheelFrontMesh
                : wheelRearMesh;

        public static readonly KartStyle Buggy = new KartStyle
        {
            name = "Buggy",
            bodyMesh = "KartBuggy_Body",
            wheelFrontMesh = "KartBuggy_WheelFront",
            wheelRearMesh = "KartBuggy_WheelRear",
            steeringWheelMesh = "KartBuggy_SteeringWheel",
        };

        public static readonly KartStyle Primitives = new KartStyle { name = "Primitives" };

        public static readonly IReadOnlyList<KartStyle> All = new[] { Buggy, Primitives };

        public static KartStyle Default => Buggy;
    }
}
