using UnityEngine;
using UnityEngine.InputSystem;

namespace Toebeans.Karting
{
    /// <summary>
    /// The kart's lamps, as one switch. Holds the real <see cref="Light"/>s that were hung on the
    /// lamp positions in <see cref="KartBlueprint.Lamps"/>, and the glass that has to change with
    /// them — a beam coming out of a lens that is still dark reads as a bug long before anyone works
    /// out which half is wrong.
    ///
    /// The glass is switched by swapping the material on one submesh rather than by writing emission
    /// onto a shared material, because the kart's materials are project assets: writing to them would
    /// light every kart in the scene, and in the Editor it would light them permanently.
    ///
    /// Off by default. LobbyIsland is a daylit map and headlights cost per-pixel lights for nothing
    /// in the middle of the day; they are for the caves, the volcano interior and dusk.
    /// </summary>
    [DisallowMultipleComponent]
    public class KartLights : MonoBehaviour
    {
        /// <summary>One submesh of one renderer — the faces that are the glass, and nothing else.</summary>
        [System.Serializable]
        public struct Lens
        {
            public Renderer renderer;
            public int submesh;
        }

        [Tooltip("Key that switches the lamps on and off.")]
        public Key toggleKey = Key.L;
        public bool onAtStart;

        [Header("Wiring — filled in by KartSetup")]
        public Light[] headlamps = new Light[0];
        public Light roofBar;
        public Lens[] lenses = new Lens[0];
        public Material lensOff;
        public Material lensLit;

        [Header("Beam")]
        public Color colour = new Color(1f, 0.96f, 0.86f);
        public float headlampIntensity = 12f;
        public float headlampRange = 55f;
        public float headlampAngle = 46f;
        [Tooltip("Degrees the nose lamps are aimed down, so the beam lands on the road rather than the horizon.")]
        public float headlampPitch = 7f;
        public float roofBarIntensity = 16f;
        public float roofBarRange = 95f;
        public float roofBarAngle = 60f;
        public float roofBarPitch = 2f;

        /// <summary>
        /// Off by default and worth leaving off. A spot light that casts shadows re-renders everything
        /// in its range into a shadow map every frame, and at 95 m in front of a moving kart that is
        /// most of the visible map — for a beam that is mostly landing on open ground anyway.
        /// </summary>
        public bool castShadows;

        bool _on;

        public bool On => _on;

        void Awake()
        {
            Apply();
            Set(onAtStart);
        }

        void OnValidate()
        {
            // Beam tuning only. Switching the glass writes to a renderer, and doing that from
            // OnValidate would dirty the prefab every time a field is nudged in the Inspector.
            Apply();
        }

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard[toggleKey].wasPressedThisFrame)
                Set(!_on);
        }

        public void Set(bool on)
        {
            _on = on;

            foreach (Light lamp in headlamps)
            {
                if (lamp != null)
                    lamp.enabled = on;
            }

            if (roofBar != null)
                roofBar.enabled = on;

            Material glass = on ? lensLit : lensOff;
            if (glass == null)
                return;

            foreach (Lens lens in lenses)
            {
                if (lens.renderer == null)
                    continue;

                Material[] slots = lens.renderer.sharedMaterials;
                if (lens.submesh < 0 || lens.submesh >= slots.Length || slots[lens.submesh] == glass)
                    continue;

                slots[lens.submesh] = glass;
                lens.renderer.sharedMaterials = slots;
            }
        }

        /// <summary>Writes the beam tuning onto the lights. Safe to call at any time.</summary>
        public void Apply()
        {
            foreach (Light lamp in headlamps)
                Configure(lamp, headlampIntensity, headlampRange, headlampAngle, headlampPitch);

            Configure(roofBar, roofBarIntensity, roofBarRange, roofBarAngle, roofBarPitch);
        }

        void Configure(Light lamp, float intensity, float range, float angle, float pitch)
        {
            if (lamp == null)
                return;

            lamp.type = LightType.Spot;
            lamp.color = colour;
            lamp.intensity = intensity;
            lamp.range = range;
            lamp.spotAngle = angle;
            // A hard-edged cone looks like a torch. Holding the inner cone at half the outer one gives
            // the beam a soft edge without another field to keep in step with the outer angle.
            lamp.innerSpotAngle = angle * 0.5f;
            lamp.shadows = castShadows ? LightShadows.Hard : LightShadows.None;
            // The lamps are parented to the kart's visual root, so local +Z is straight ahead and a
            // positive X rotation tips the beam down onto the road.
            lamp.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }
    }
}
