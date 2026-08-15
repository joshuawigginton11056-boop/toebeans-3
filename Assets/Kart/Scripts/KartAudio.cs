using UnityEngine;

namespace Toebeans.Karting
{
    /// <summary>
    /// Engine and exhaust sound for the kart, synthesised at runtime — the project ships no audio
    /// files, and an engine note is a genuinely good fit for synthesis: it *is* a stack of harmonics
    /// of the firing frequency, so building it that way is closer to the real thing than looping a
    /// recording of somebody else's kart.
    ///
    /// Two voices, because one never sounds right: a bright mechanical voice at the engine, and a
    /// darker, rougher one at the exhaust tip. Both are pitched by the same engine speed, so they stay
    /// locked together the way two microphones on one engine would.
    /// </summary>
    [DisallowMultipleComponent]
    public class KartAudio : MonoBehaviour
    {
        [Header("Sources")]
        public AudioSource engineSource;
        public AudioSource exhaustSource;

        [Header("Mix")]
        [Range(0f, 1f)] public float engineVolume = 0.45f;
        [Range(0f, 1f)] public float exhaustVolume = 0.60f;
        [Tooltip("Overall pitch trim, if the engine sounds too high or low strung for the kart.")]
        [Range(0.5f, 2f)] public float pitchTrim = 1f;

        [Header("Character")]
        [Tooltip("Engine speed the clips are baked at. Playback pitch is engine rpm divided by this, so " +
                 "it also sets how far the pitch has to travel between idle and the limiter.")]
        public float referenceRpm = 3500f;
        [Tooltip("Extra roughness on the exhaust voice. 0 is a clean tone, 1 is a blare.")]
        [Range(0f, 1f)] public float exhaustGrit = 0.55f;

        [Header("Falloff")]
        public float minDistance = 3f;
        public float maxDistance = 120f;
        [Range(0f, 1f)] public float dopplerLevel = 0.3f;

        const int SampleRate = 44100;
        /// <summary>Whole cycles baked into the loop. More gives the noise room to breathe.</summary>
        const int CyclesPerClip = 8;

        KartController _kart;

        void Awake()
        {
            _kart = GetComponent<KartController>() ?? GetComponentInParent<KartController>();

            // A kart is a two-stroke: one firing event per revolution, so the fundamental is simply
            // rpm/60. The clip length is locked to a whole number of cycles, which is what lets it loop
            // without a click.
            int samplesPerCycle = Mathf.RoundToInt(SampleRate * 60f / Mathf.Max(referenceRpm, 1f));

            engineSource = Configure(engineSource, "Engine", new Vector3(0f, 0.56f, -1.00f));
            exhaustSource = Configure(exhaustSource, "Exhaust", new Vector3(0.14f, 1.02f, -1.16f));

            // Bright and mechanical: strong upper orders, mild roughness.
            engineSource.clip = BuildVoice("KartEngineTone", samplesPerCycle, 26, 0.90f, 0.25f, seed: 17);
            // Dark and blaring: the low orders carry it, with far more noise between them.
            exhaustSource.clip = BuildVoice("KartExhaustTone", samplesPerCycle, 18, 1.45f, exhaustGrit, seed: 91);

            engineSource.Play();
            exhaustSource.Play();
        }

        void Update()
        {
            if (_kart == null || engineSource == null || exhaustSource == null)
                return;

            float pitch = Mathf.Max(0.05f, _kart.EngineRpm / Mathf.Max(referenceRpm, 1f)) * pitchTrim;
            // AudioSource refuses to go beyond ±3, and a clamp here is quieter than the engine
            // silently cutting out at the top of the rev range.
            pitch = Mathf.Clamp(pitch, 0.05f, 3f);

            float load = _kart.EngineLoad;
            float throttle = Mathf.Clamp01(_kart.ThrottleInput + _kart.ReverseInput);

            engineSource.pitch = pitch;
            exhaustSource.pitch = pitch;

            // The engine voice tracks revs; the exhaust also tracks throttle, so lifting off drops the
            // blare and leaves the mechanical noise behind — which is most of what "coasting" sounds like.
            engineSource.volume = engineVolume * Mathf.Lerp(0.30f, 1f, load);
            exhaustSource.volume = exhaustVolume
                                   * Mathf.Lerp(0.20f, 1f, load)
                                   * Mathf.Lerp(0.45f, 1f, throttle);
        }

        AudioSource Configure(AudioSource existing, string name, Vector3 localPosition)
        {
            AudioSource source = existing;

            if (source == null)
            {
                var go = new GameObject($"Audio {name}");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = localPosition;
                source = go.AddComponent<AudioSource>();
            }

            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = minDistance;
            source.maxDistance = maxDistance;
            source.dopplerLevel = dopplerLevel;
            return source;
        }

        /// <summary>
        /// Builds one looping voice as a stack of harmonics over <paramref name="samplesPerCycle"/>.
        ///
        /// Everything here is a whole number of cycles across the clip, including the roughness — which
        /// is itself built from high harmonics at random phases rather than from actual random samples.
        /// White noise cannot loop; a harmonic stack always can, and at these densities it is
        /// indistinguishable from noise by ear.
        /// </summary>
        static AudioClip BuildVoice(string name, int samplesPerCycle, int harmonics, float rolloff,
            float grit, int seed)
        {
            float[] data = BuildSamples(samplesPerCycle, harmonics, rolloff, grit, seed);
            var clip = AudioClip.Create(name, data.Length, 1, SampleRate, stream: false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>
        /// The waveform itself, kept free of any Unity object so the loop can be checked outside the
        /// Editor. A seam at the loop point is a click once per cycle, and that is the one defect here
        /// that no amount of looking at the code reveals.
        /// </summary>
        public static float[] BuildSamples(int samplesPerCycle, int harmonics, float rolloff,
            float grit, int seed)
        {
            int count = samplesPerCycle * CyclesPerClip;
            var data = new float[count];
            var random = new System.Random(seed);

            // Random but fixed phases stop the harmonics stacking into one buzzy spike at the loop point.
            var phases = new float[harmonics + 1];
            for (int h = 1; h <= harmonics; h++)
                phases[h] = (float)(random.NextDouble() * Mathf.PI * 2f);

            for (int h = 1; h <= harmonics; h++)
            {
                float amplitude = 1f / Mathf.Pow(h, rolloff);

                // Orders above the fundamental are where an engine's rasp lives, so let grit lift them
                // unevenly rather than raising the whole stack.
                if (h > 1)
                    amplitude *= Mathf.Lerp(1f, 0.4f + (float)random.NextDouble() * 1.6f, grit);

                float step = 2f * Mathf.PI * h / samplesPerCycle;
                float phase = phases[h];

                for (int i = 0; i < count; i++)
                    data[i] += amplitude * Mathf.Sin(step * i + phase);
            }

            Normalise(data);

            // A little saturation. Engines do not produce clean sine stacks, and softly squashing the
            // peaks is what turns a hum into something with an edge on it.
            for (int i = 0; i < count; i++)
                data[i] = SoftClip(data[i] * 1.6f);

            Normalise(data);
            return data;
        }

        static void Normalise(float[] data)
        {
            float peak = 0f;
            foreach (float sample in data)
                peak = Mathf.Max(peak, Mathf.Abs(sample));

            if (peak < 1e-6f)
                return;

            float scale = 0.98f / peak;
            for (int i = 0; i < data.Length; i++)
                data[i] *= scale;
        }

        static float SoftClip(float x) => x / (1f + Mathf.Abs(x));
    }
}
