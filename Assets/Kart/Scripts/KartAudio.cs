using UnityEngine;

namespace Toebeans.Karting
{
    /// <summary>
    /// Engine and exhaust sound for the kart, synthesised at runtime — the project ships no audio
    /// files, so this has to build its own or the kart is silent.
    ///
    /// This is a pulse model, not a harmonic stack, and that is the whole point of it. The previous
    /// version summed steady sine waves at multiples of the firing frequency, which is a defensible
    /// description of an engine's *spectrum* and sounds nothing like one, because an engine is not a
    /// steady tone. It is a series of explosions. What reaches your ear is one sharp pressure pulse
    /// per firing event, each one setting the exhaust ringing and dying away before the next arrives,
    /// and it is that rise-and-fall — the gap between the bangs as much as the bangs — that the ear
    /// uses to tell a motor from an organ. Summed sines have no gaps in them at all.
    ///
    /// So each firing event here is modelled as it behaves: an instantaneous kick, a burst of
    /// combustion noise, and two or three damped resonances ringing out at the frequencies the pipe
    /// actually resonates at. Everything else follows from that.
    ///
    /// Two voices, because one never sounds right: a hard, fast, clattery one at the engine, and a
    /// slow booming one at the exhaust tip. Both fire on the same schedule, so they stay locked
    /// together the way two microphones on one engine would.
    ///
    /// KNOWN LIMIT, and the reason this is a staging post rather than a destination: one clip pitched
    /// across the whole rev range drags its resonances along with it. A real exhaust pipe is a fixed
    /// length and rings at a fixed frequency no matter how fast the engine turns, but pitch-shifting
    /// a recording moves that frequency, so the kart sounds like a physically larger engine at idle
    /// and a smaller one at the limiter. That artefact is most of what "fake" means in game engine
    /// audio, and no amount of waveform work fixes it — the fix is several clips at stepped rpm,
    /// crossfaded so each is only ever shifted a little. <see cref="referenceRpm"/> is set to the
    /// middle of the usable range to keep the shift as small and as symmetric as one clip allows.
    /// </summary>
    [DisallowMultipleComponent]
    public class KartAudio : MonoBehaviour
    {
        [Header("Sources")]
        public AudioSource engineSource;
        public AudioSource exhaustSource;

        [Header("Mix")]
        [Range(0f, 1f)] public float engineVolume = 0.42f;
        [Range(0f, 1f)] public float exhaustVolume = 0.9f;
        [Tooltip("Overall pitch trim, if the engine sounds too high or low strung for the kart.")]
        [Range(0.5f, 2f)] public float pitchTrim = 1f;

        [Header("Character")]
        [Tooltip("Engine speed the clips are baked at, and the one rpm where the resonances below are " +
                 "heard at their authored frequency. Sits in the middle of the usable rev range on " +
                 "purpose: everything either side of it is pitch-shifted, and the further a clip is " +
                 "shifted the more obviously synthetic it sounds.")]
        public float referenceRpm = 4400f;
        [Tooltip("Firing events per crankshaft revolution. 1 is a two-stroke and is why this used to " +
                 "sound like a strimmer. 0.5 is a four-stroke — one bang every other revolution, an " +
                 "octave lower for the same rev counter, and half as many pulses per second, which is " +
                 "as much of the character as the pitch is.")]
        [Range(0.25f, 4f)] public float firingsPerRevolution = 0.5f;
        [Tooltip("How long each firing rings out, as a fraction of the gap between firings. Low is a " +
                 "tight percussive bark with silence between the bangs; high runs each pulse into the " +
                 "next for a continuous drone. Note this is a fixed duty cycle, so unlike a real " +
                 "engine the bangs never blur together at the top of the rev range — which is one " +
                 "more thing the multi-clip player fixes and this cannot.")]
        [Range(0.08f, 0.9f)] public float pulseLength = 0.45f;
        [Tooltip("Combustion noise on each firing — the chuff riding on top of the pressure pulse. " +
                 "0 is a clean resonance, 1 is all blare.")]
        [Range(0f, 1f)] public float exhaustGrit = 0.55f;
        [Tooltip("Gain on the lowest exhaust resonance. This is the chest-thump, and the single knob " +
                 "with most say in whether the motor reads as big or as busy.")]
        [Range(0f, 2f)] public float exhaustBoom = 1.35f;
        [Tooltip("How unevenly the firings land, 0 to 1. A real single does not produce eight " +
                 "identical bangs in a row, and a clip that does reads as a loop rather than as an " +
                 "engine — this is what stops the ear locking onto the eight-cycle repeat.")]
        [Range(0f, 0.6f)] public float lumpiness = 0.22f;

        [Header("Falloff")]
        public float minDistance = 3f;
        public float maxDistance = 120f;
        [Range(0f, 1f)] public float dopplerLevel = 0.3f;

        const int SampleRate = 44100;
        /// <summary>Firing events baked into the loop. More gives the unevenness room to breathe.</summary>
        const int CyclesPerClip = 8;

        KartController _kart;

        void Awake()
        {
            _kart = GetComponent<KartController>() ?? GetComponentInParent<KartController>();

            // Samples between one firing and the next at the reference speed. The firing count goes in
            // here rather than into the playback pitch, so pitch still tracks rpm/referenceRpm exactly
            // and a four-stroke simply gets a longer gap between its bangs.
            int samplesPerCycle = Mathf.RoundToInt(
                SampleRate * 60f / Mathf.Max(referenceRpm * firingsPerRevolution, 1f));

            engineSource = Configure(engineSource, "Engine", new Vector3(0f, 0.56f, -1.00f));
            exhaustSource = Configure(exhaustSource, "Exhaust", new Vector3(0.14f, 1.02f, -1.16f));

            // Mechanical clatter: high, tight, quick to die. This is valve gear and piston slap, the
            // noise the engine makes that never went near the exhaust.
            engineSource.clip = BuildVoice("KartEngineTone", samplesPerCycle,
                modeHz: new[] { 320f, 760f, 1580f },
                modeGain: new[] { 1f, 0.55f, 0.28f },
                pulseLength: pulseLength * 0.45f, grit: 0.3f, lumpiness: lumpiness, seed: 17);

            // The pipe: low, loud, slow to die. This is the voice doing the "big engine" work, which
            // is why it also carries the boom and most of the level.
            exhaustSource.clip = BuildVoice("KartExhaustTone", samplesPerCycle,
                modeHz: new[] { 88f, 187f, 404f },
                modeGain: new[] { exhaustBoom, 0.62f, 0.3f },
                pulseLength: pulseLength, grit: exhaustGrit, lumpiness: lumpiness, seed: 91);

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

        static AudioClip BuildVoice(string name, int samplesPerCycle, float[] modeHz, float[] modeGain,
            float pulseLength, float grit, float lumpiness, int seed)
        {
            float[] data = BuildPulseTrain(samplesPerCycle, SampleRate, modeHz, modeGain,
                pulseLength, grit, lumpiness, seed);
            var clip = AudioClip.Create(name, data.Length, 1, SampleRate, stream: false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>
        /// The waveform itself: one damped pressure pulse per firing event, repeated across the loop.
        ///
        /// Kept free of any Unity object so the loop can be checked outside the Editor. A seam at the
        /// loop point is a click once per cycle, and that is the one defect here that no amount of
        /// looking at the code reveals. Nothing seams as long as each pulse has decayed to silence
        /// before the next firing is due, which is what <paramref name="pulseLength"/> guarantees by
        /// being expressed as a fraction of the gap rather than in absolute milliseconds — a long gap
        /// gets a long ring and a short one gets a short ring, and neither ever runs off the end.
        /// </summary>
        /// <param name="modeHz">Resonant frequencies excited by each firing, at the reference speed.</param>
        /// <param name="modeGain">Relative loudness of each resonance. Index-matched to modeHz.</param>
        /// <param name="pulseLength">Ring-out time as a fraction of the gap between firings.</param>
        /// <param name="lumpiness">How much firing-to-firing variation, 0 to 1.</param>
        public static float[] BuildPulseTrain(int samplesPerCycle, int sampleRate, float[] modeHz,
            float[] modeGain, float pulseLength, float grit, float lumpiness, int seed)
        {
            samplesPerCycle = Mathf.Max(samplesPerCycle, 2);
            int count = samplesPerCycle * CyclesPerClip;
            var data = new float[count];
            var random = new System.Random(seed);

            // Decay constant that puts the pulse at 1% of its peak after pulseLength of the gap.
            // Anchoring the ring-out to the gap rather than to the clock is what keeps the loop seam
            // clean at every firing rate this is ever built at.
            float ringSamples = Mathf.Max(samplesPerCycle * Mathf.Clamp(pulseLength, 0.02f, 0.95f), 1f);
            float decayPerSample = 4.6f / ringSamples;

            // Rise time. A pulse that jumps from silence to full amplitude in one sample is a step,
            // and a step is broadband — it reads as a digital tick laid over the note rather than as
            // the front of a bang. Real cylinder pressure takes a moment to get out of the port, and
            // giving it about half a millisecond here is the difference between a thump and a click.
            float attackSamples = Mathf.Max(ringSamples * 0.06f, 8f);

            // Combustion chuff: a fixed burst of noise at the head of every firing. Fixed rather than
            // freshly random per cycle because white noise cannot loop — reused, it reads as the same
            // engine breathing rather than as a repeat, and the per-firing gain below hides the rest.
            int chuffLength = Mathf.Max(Mathf.Min(samplesPerCycle / 3, sampleRate / 150), 1);
            var chuff = new float[chuffLength];
            for (int j = 0; j < chuffLength; j++)
            {
                float fade = Mathf.Exp(-j / (chuffLength * 0.3f));
                chuff[j] = ((float)random.NextDouble() * 2f - 1f) * fade;
            }

            // Per-firing gain and a sub-sample of timing wander. Eight identical bangs in a row is the
            // sound of a loop; a real single wanders, and the ear stops hearing the eight-cycle repeat
            // the moment it does.
            var cycleGain = new float[CyclesPerClip];
            var cycleSkew = new float[CyclesPerClip];
            for (int c = 0; c < CyclesPerClip; c++)
            {
                cycleGain[c] = 1f - lumpiness * (float)random.NextDouble();
                cycleSkew[c] = 1f + lumpiness * 0.25f * ((float)random.NextDouble() * 2f - 1f);
            }

            int modes = Mathf.Min(modeHz.Length, modeGain.Length);

            for (int i = 0; i < count; i++)
            {
                int cycle = i / samplesPerCycle;
                int since = i % samplesPerCycle;

                float envelope = Mathf.Exp(-decayPerSample * since)
                                 * (1f - Mathf.Exp(-since / attackSamples));
                // Below audibility, and skipping it keeps the gaps between bangs genuinely quiet.
                if (envelope < 1e-4f && since > attackSamples)
                    continue;

                float seconds = since / (float)sampleRate * cycleSkew[cycle];

                float value = 0f;
                for (int m = 0; m < modes; m++)
                    value += modeGain[m] * Mathf.Sin(2f * Mathf.PI * modeHz[m] * seconds);

                if (since < chuffLength)
                    value += grit * chuff[since] * 2.2f;

                data[i] = value * envelope * cycleGain[cycle];
            }

            Normalise(data);

            // Saturation. Squashing the peaks folds energy down into the low orders, which is most of
            // why a driven signal reads as bigger rather than merely louder.
            for (int i = 0; i < count; i++)
                data[i] = SoftClip(data[i] * 2.2f);

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
