using System.Collections.Generic;
using UnityEngine;

namespace Toebeans.Karting
{
    /// <summary>
    /// One slot's appearance: what <see cref="KartStyle.palette"/> holds for each
    /// <see cref="KartSkin"/>. Plain data, so it can live in the runtime assembly and be filled in by
    /// the Editor from a Blender manifest.
    /// </summary>
    public struct KartSkinColour
    {
        public Color color;
        public float metallic;
        public float smoothness;

        /// <summary>
        /// Black for everything that does not glow. Written past 1.0 when it does, the same way
        /// KartLensLit is — a fissure that only reaches white reads as pale paint, not as heat.
        /// </summary>
        public Color emission;
    }

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
        /// Identifier used for asset paths and to find this style's Blender manifest at
        /// <c>Assets/GeneratedModels/Manifests/kart_&lt;key&gt;.json</c>. No spaces: it becomes part of
        /// the generated material names, so it has to be stable and path-safe.
        /// </summary>
        public string key;

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

        /// <summary>
        /// Whether this style's bodywork has lamp housings in it, and so whether the kart gets a
        /// working set of lights. The housings and their glass are part of the model — the runtime
        /// only adds the Lights and the switch — so a style whose model has no lamps has to leave
        /// this off, or the kart drives around with beams coming out of thin air.
        ///
        /// The lamp positions themselves are not a style setting: they live in KartBlueprint, where
        /// the model script can assert against them. See <see cref="KartBlueprint.Lamps"/>.
        /// </summary>
        public bool headlights;

        /// <summary>
        /// Which of KartBlueprint's two lamp clusters this style's bodywork actually contains.
        ///
        /// The buggy has both — a nose pair and a four-pod roof bar — and so needed no distinction.
        /// The rest of the pack does: the mine cart carries one big carbide lamp on the roof bar
        /// point and no nose lamps at all, and the bone chariot has embers in its eye sockets and no
        /// roof bar to hang pods from. Building a Light for a cluster the model has no housings for
        /// is a beam coming out of empty air, which is the same fault <see cref="headlights"/> exists
        /// to prevent, one level down.
        ///
        /// Both default true, so <see cref="Buggy"/> and any style shaped like it need not say.
        /// Ignored entirely when <see cref="headlights"/> is off.
        /// </summary>
        public bool noseLamps = true;
        public bool roofBar = true;

        /// <summary>
        /// This style's colours, keyed by skin. Null means "use the shared default palette", which is
        /// what <see cref="Primitives"/> wants and what any style gets before its manifest is read.
        ///
        /// Filled in by the Editor from the Blender manifest rather than written here — see
        /// KartStyleManifest. The numbers are authored once, in the style's Python file, because
        /// forty-odd colour and roughness values copied across a language boundary is exactly the
        /// drift the farm pack's manifests were introduced to stop.
        /// </summary>
        public Dictionary<KartSkin, KartSkinColour> palette;

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

        /// <summary>Every mesh-based style, built from one Blender script each under Tools/blender.</summary>
        static KartStyle Mesh(string name, string key, string prefix, bool headlights,
            bool noseLamps = true, bool roofBar = true)
        {
            return new KartStyle
            {
                name = name,
                key = key,
                bodyMesh = $"{prefix}_Body",
                wheelFrontMesh = $"{prefix}_WheelFront",
                wheelRearMesh = $"{prefix}_WheelRear",
                steeringWheelMesh = $"{prefix}_SteeringWheel",
                headlights = headlights,
                noseLamps = noseLamps,
                roofBar = roofBar,
            };
        }

        public static readonly KartStyle Buggy =
            Mesh("Buggy", "Buggy", "KartBuggy", headlights: true);

        // Snow. Nose lamps ride on the cowl above the plow blade; no roof bar to hang pods from.
        public static readonly KartStyle PisteBasher =
            Mesh("Piste basher", "Piste", "KartPiste", headlights: true, roofBar: false);

        // Cave. One carbide lamp sitting on KartBlueprint's roof bar point, and no nose pair — see
        // the header of Tools/blender/models/mine_cart.py for why that is the only place it can go.
        public static readonly KartStyle MineCart =
            Mesh("Mine cart", "Mine", "KartMine", headlights: true, noseLamps: false);

        // Farm. Tractor lamps either side of the grille.
        public static readonly KartStyle FieldMarshal =
            Mesh("Field marshal", "Field", "KartField", headlights: true, roofBar: false);

        // Lava. No headlights, and that is load-bearing rather than an omission: this style spends
        // the KartLens slot on the glowing fissures in its crust, and KartLights switches lamps on by
        // swapping the material on every KartLens face. Turn headlights on here and the whole body
        // flares on the L key.
        public static readonly KartStyle CinderHauler =
            Mesh("Cinder hauler", "Cinder", "KartCinder", headlights: false);

        // Jungle. Bamboo and one enormous leaf; no lamps anywhere on it.
        public static readonly KartStyle Overgrowth =
            Mesh("Overgrowth", "Overgrowth", "KartOvergrowth", headlights: false);

        // Woodland. Antlers and cross-cut log wheels; no lamps.
        public static readonly KartStyle LogRacer =
            Mesh("Log racer", "Log", "KartLog", headlights: false);

        // Hell, alternate. Embers in the skull's eye sockets, on the nose lamp points.
        public static readonly KartStyle BoneChariot =
            Mesh("Bone chariot", "Bone", "KartBone", headlights: true, roofBar: false);

        // Universal unlock. Scrap kart; keeps a mismatched pair of nose lamps.
        public static readonly KartStyle PitRat =
            Mesh("Pit rat", "PitRat", "KartPitRat", headlights: true, roofBar: false);

        // No headlights: the primitive kart has no cage and no prow to mount them on, and lamps
        // hovering where the buggy's roof bar would be is worse than no lamps at all.
        public static readonly KartStyle Primitives =
            new KartStyle { name = "Primitives", key = "Primitives" };

        public static readonly IReadOnlyList<KartStyle> All = new[]
        {
            Buggy, PisteBasher, MineCart, FieldMarshal, CinderHauler,
            Overgrowth, LogRacer, BoneChariot, PitRat, Primitives,
        };

        public static KartStyle Default => Buggy;
    }
}
