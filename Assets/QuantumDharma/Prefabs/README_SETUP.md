# Quantum Dharma NPC — Prefab Setup Guide

Step-by-step instructions for setting up the NPC in a VRChat world.

---

## Prerequisites

- Unity 2022.3 LTS
- VRChat Creator Companion (VCC) with Worlds SDK imported
- UdonSharp package imported via VCC

---

## 1. GameObject Hierarchy

Create the following hierarchy in your scene:

```
QuantumDharmaNPC              ← Empty GameObject (root)
├── Model                     ← Your NPC mesh/avatar model (+ Animator)
├── MarkovBlanket             ← Empty GameObject
├── PlayerSensor              ← Empty GameObject
├── HandProximityDetector     ← Empty GameObject (optional)
├── PostureDetector           ← Empty GameObject (optional)
├── TouchSensor               ← Empty GO + SphereCollider (trigger) (optional)
├── GiftReceiver              ← Empty GameObject (optional)
├── DreamState                ← Empty GameObject (optional)
├── MirrorBehavior            ← Empty GameObject (optional)
├── ContextualUtterance       ← Empty GameObject (optional)
├── ProximityAudio            ← Empty GO + AudioSource (optional)
├── VoiceDetector             ← Empty GameObject (optional)
├── DreamNarrative            ← Empty GameObject (optional)
├── AdaptivePersonality       ← Empty GameObject (optional)
├── IdleWaypoints             ← Empty GO + child Transforms (optional)
├── TrustVisualizer           ← Empty GameObject (optional)
├── CuriosityDrive            ← Empty GameObject (optional)
├── GestureController         ← Empty GameObject (optional, needs Animator ref)
├── GroupDynamics             ← Empty GameObject (optional)
├── EmotionalContagion        ← Empty GameObject (optional)
├── AttentionSystem           ← Empty GameObject (optional)
├── HabitFormation            ← Empty GameObject (optional)
├── MultiNPCRelay             ← Empty GameObject (optional)
├── SharedRitual              ← Empty GameObject (optional)
├── CollectiveMemory          ← Empty GameObject (optional)
├── GiftEconomy               ← Empty GameObject (optional)
├── NormFormation              ← Empty GameObject (optional)
├── OralHistory               ← Empty GameObject (optional)
├── NameGiving                ← Empty GameObject (optional)
├── Mythology                 ← Empty GameObject (optional)
├── NPCMotor                  ← Empty GameObject
├── LookAtController          ← Empty GameObject (optional, needs Animator ref)
├── EmotionAnimator           ← Empty GameObject (optional, needs Animator ref)
├── SessionMemory             ← Empty GameObject (optional)
├── QuantumDharmaManager      ← Empty GameObject
├── FreeEnergyVisualizer      ← Empty GameObject + LineRenderer
└── DebugCanvas               ← World-space Canvas
    └── DebugPanel             ← Panel with UI elements
        ├── StateLabel         ← Text (state name)
        ├── FreeEnergyLabel    ← Text (F value)
        ├── TrustLabel         ← Text (trust level)
        ├── RadiusLabel        ← Text (blanket radius)
        ├── DetailsLabel       ← Text (PE breakdown + motor info)
        └── StateBackground    ← Image (color-coded backdrop)
```

---

## 2. Component Setup

### 2a. Root: QuantumDharmaNPC

No special components needed on the root. Position this where you want the NPC to spawn. The NPC's roam area is centered on this position.

### 2b. MarkovBlanket

1. Add **UdonBehaviour** (automatically added when you add the script)
2. Attach script: `MarkovBlanket.cs`
3. Configure in Inspector:
   - **Min Radius:** 3 (contracted state — low trust)
   - **Max Radius:** 15 (expanded state — high trust)
   - **Default Radius:** 8 (neutral starting radius)
   - **Trust Decay Rate:** 0.05 (regression speed to baseline)
   - **Show Gizmo:** true (editor visualization)

### 2c. PlayerSensor

1. Add **UdonBehaviour**
2. Attach script: `PlayerSensor.cs`
3. Configure in Inspector:
   - **Detection Radius:** 10 (fallback if no MarkovBlanket)
   - **Poll Interval:** 0.25 (seconds between scans)
   - **Markov Blanket:** drag the MarkovBlanket GameObject
   - **Manager:** drag the QuantumDharmaManager GameObject
   - **Hand Proximity Detector:** (optional) drag the HandProximityDetector GameObject
   - **Posture Detector:** (optional) drag the PostureDetector GameObject
   - **Voice Detector:** (optional) drag the VoiceDetector GameObject

### 2d. HandProximityDetector (optional)

1. Create an empty child GameObject under the NPC root named `HandProximityDetector`
2. Add **UdonBehaviour**
3. Attach script: `HandProximityDetector.cs`
4. Position this GameObject at the NPC's center (or same as the NPC root)
5. Configure in Inspector:
   - **Player Sensor:** drag the PlayerSensor GameObject
   - **Reach Threshold:** 1.5 (max hand distance for "reaching out" classification)
   - **Min Body Distance:** 1.0 (body must be at least this far for "reaching out")
   - **Poll Interval:** 0.25

### 2e. PostureDetector (optional)

1. Create an empty child GameObject under the NPC root named `PostureDetector`
2. Add **UdonBehaviour**
3. Attach script: `PostureDetector.cs`
4. Configure in Inspector:
   - **Player Sensor:** drag the PlayerSensor GameObject
   - **Crouch Threshold:** 0.7 (head/eye ratio below which = crouching)
   - **Crouch Kindness Multiplier:** 1.5 (trust boost when crouching)
   - **Poll Interval:** 0.25

### 2f. TouchSensor (optional)

1. Create an empty child GameObject under the NPC root named `TouchSensor`
2. Add a **SphereCollider**:
   - **Is Trigger:** true
   - **Radius:** 1.0 (personal space radius in meters)
3. Add a **Rigidbody**:
   - **Is Kinematic:** true
   - **Use Gravity:** false
4. Add **UdonBehaviour**
5. Attach script: `TouchSensor.cs`
6. Configure in Inspector:
   - **Markov Blanket:** drag the MarkovBlanket GameObject
   - **NPC Transform:** drag the QuantumDharmaNPC root GameObject
   - **Head Zone Height:** 1.5 (height above NPC pivot for head zone)
   - **Back Zone Threshold:** -0.3 (dot product below which = behind NPC)
   - **Comfort Trust Threshold:** 0.3 (trust level above which touch is welcomed)
   - **Comfort Trust Boost:** 0.15 (trust gain per sec for head touch)
   - **Greeting Trust Boost:** 0.08 (trust gain per sec for hand touch)
   - **Startle Trust Penalty:** -0.1 (trust loss for low-trust touch)
   - **Push Trust Penalty:** -0.15 (trust loss for back push)
   - **Touch Cooldown:** 2.0 (seconds between events from same player)
   - **Prolonged Threshold:** 1.5 (seconds before prolonged effect)
   - **Prolonged Multiplier:** 2.0

### 2f-i. GiftReceiver (optional)

1. Create an empty child GameObject under the NPC root named `GiftReceiver`
2. Add **UdonBehaviour**
3. Attach script: `GiftReceiver.cs`
4. Configure in Inspector:
   - **NPC Transform:** drag the QuantumDharmaNPC root GameObject
   - **Markov Blanket:** drag the MarkovBlanket GameObject
   - **Gift Pickup Objects:** drag all pickup GameObjects that can be offered as gifts
   - **Gift Radius:** 2.5 (max distance for dropped pickup to count as gift)
   - **Poll Interval:** 0.5 (seconds between pickup state checks)
   - **Gift Trust Boost:** 0.25 (base trust gain per gift)
   - **First Gift Bonus:** 0.20 (extra trust for first gift from a player)
   - **Habituation Factor:** 0.6 (diminishing returns multiplier)
   - **Min Gift Boost:** 0.03 (minimum trust even after full habituation)
   - **Gift Burst Particles:** (optional) drag a ParticleSystem for burst effect
   - **Burst Count:** 30
5. **Gift pickup setup:**
   - Each gift-eligible object needs a **VRC_Pickup** component
   - Add a **Rigidbody** (not kinematic) and **Collider** to each pickup
   - Wire each pickup's GameObject into the Gift Pickup Objects array

### 2g. DreamState (optional)

1. Create an empty child GameObject under the NPC root named `DreamState`
2. Add **UdonBehaviour**
3. Attach script: `DreamState.cs`
4. Configure in Inspector:
   - **Player Sensor:** drag the PlayerSensor GameObject
   - **Session Memory:** drag the SessionMemory GameObject
   - **Dream Particles:** (optional) drag a ParticleSystem for dream visualization
   - **Dream Emission Rate:** 3 (particles/sec while dreaming)
   - **Dream Color:** translucent purple (0.5, 0.4, 0.8, 0.4)
   - **Sleep Delay:** 5 (seconds without players before entering drowsy)
   - **Drowsy Duration:** 3 (transition time drowsy → dreaming)
   - **Wake Duration:** 1.5 (disorientation time on player return)
   - **Consolidation Interval:** 2 (seconds between belief consolidation ticks)
   - **Trust Normalize Target:** 0.3 (extreme trust regresses toward this)
   - **Trust Normalize Rate:** 0.01 (per consolidation tick)
   - **Friend Kindness Boost:** 0.1 (kindness reinforcement per tick)
   - **Forgive Rate:** 0.02 (negative trust softening per tick)
5. **Dream particles setup:**
   - Create a child ParticleSystem on DreamState or the NPC root
   - Set **Start Speed** low (0.1–0.3) and **Start Lifetime** long (3–5s)
   - Use a soft, translucent material (additive or alpha blend)
   - The script controls emission rate and color at runtime

### 2g-i. MirrorBehavior (optional)

1. Create an empty child GameObject under the NPC root named `MirrorBehavior`
2. Add **UdonBehaviour**
3. Attach script: `MirrorBehavior.cs`
4. Configure in Inspector:
   - **Manager:** drag the QuantumDharmaManager GameObject
   - **Posture Detector:** drag the PostureDetector GameObject
   - **Player Sensor:** drag the PlayerSensor GameObject
   - **Belief State:** drag the BeliefState GameObject
   - **Model Transform:** drag the Model GameObject (NPC mesh root)
   - **Animator:** (optional) drag the Animator on the Model
   - **Mirror Trust Threshold:** 0.5 (minimum trust to activate)
   - **Max Crouch Drop:** 0.6 (meters of Y drop for full mirror)
   - **Max Lean Angle:** 10 (degrees of forward tilt)
   - **Mirror Smooth Speed:** 3 (interpolation speed)
5. **Animator parameters (optional):**
   - Create float parameters: `MirrorCrouch` (0-1) and `MirrorLean` (0-1)
   - These can drive additional animation blend for crouching/leaning

### 2g-ii. ContextualUtterance (optional)

1. Create an empty child GameObject under the NPC root named `ContextualUtterance`
2. Add **UdonBehaviour**
3. Attach script: `ContextualUtterance.cs`
4. Configure in Inspector:
   - **NPC:** drag the QuantumDharmaNPC GameObject
   - **Manager:** drag the QuantumDharmaManager GameObject
   - **Session Memory:** drag the SessionMemory GameObject
   - **Dream State:** (optional) drag the DreamState GameObject
   - **Contextual Cooldown:** 12 (seconds between contextual utterances)
   - **Long Presence Threshold:** 60 (seconds of co-presence for "you stayed" trigger)
   - **Display Duration:** 5 (seconds text stays visible)

### 2g-iii. ProximityAudio (optional)

1. Create an empty child GameObject under the NPC root named `ProximityAudio`
2. Add an **AudioSource** component:
   - **Spatial Blend:** 1.0 (full 3D)
   - **Loop:** true
   - **Play On Awake:** false (script controls playback)
   - Assign a looping ambient clip (humming, breathing, soft tone)
3. Add **UdonBehaviour**
4. Attach script: `ProximityAudio.cs`
5. Configure in Inspector:
   - **NPC:** drag the QuantumDharmaNPC GameObject
   - **Manager:** drag the QuantumDharmaManager GameObject
   - **Dream State:** (optional) drag the DreamState GameObject
   - **Audio Source:** drag this GameObject's AudioSource component
   - **Volume (per emotion):** Calm 0.08, Curious 0.12, Warm 0.15, Anxious 0.25, Grateful 0.18
   - **Pitch (per emotion):** Calm 1.0, Curious 1.08, Warm 0.92, Anxious 1.35, Grateful 0.88
   - **Volume Dream:** 0.04, **Pitch Dream:** 0.7
   - **Intimate Distance:** 1.5 (full volume range)
   - **Max Audible Distance:** 5.0 (zero volume beyond this)
6. **Audio clip suggestions:**
   - A soft, loopable ambient sound (2–10 seconds)
   - Gentle humming, slow breathing, or tonal drone
   - Pitch/volume modulation by the script creates emotional variation

### 2g-iv. VoiceDetector (optional)

1. Create an empty child GameObject under the NPC root named `VoiceDetector`
2. Add **UdonBehaviour**
3. Attach script: `VoiceDetector.cs`
4. Configure in Inspector:
   - **Player Sensor:** drag the PlayerSensor GameObject
   - **Engagement Distance:** 5 (max distance for engagement detection)
   - **Gaze Threshold:** 0.5 (player must face NPC above this dot product)
   - **Stillness Threshold:** 0.5 (max speed in m/s for "still" classification)
   - **Close Distance:** 2 (distance that amplifies signal)
   - **Poll Interval:** 0.25 (seconds between engagement checks)
   - **Smooth Speed:** 4 (signal smoothing per second)
5. **Note:** VRChat UdonSharp has no mic amplitude API. This detector uses a behavioral engagement proxy (proximity + gaze + stillness) to estimate voice activity. If VRChat adds voice APIs in the future, this can be upgraded while keeping the same signal interface.

### 2g-v. DreamNarrative (optional)

1. Create an empty child GameObject under the NPC root named `DreamNarrative`
2. Add **UdonBehaviour**
3. Attach script: `DreamNarrative.cs`
4. Configure in Inspector:
   - **Dream State:** drag the DreamState GameObject
   - **Session Memory:** drag the SessionMemory GameObject
   - **NPC:** drag the QuantumDharmaNPC GameObject
   - **Narrative Delay:** 3.5 (seconds after wake before narrative appears)
   - **Display Duration:** 6 (seconds narrative stays visible)
   - **Min Dream Duration:** 10 (minimum dream length to trigger narrative)
   - **Warm Friend Ratio:** 0.4 (friend ratio above which = warm dream)
   - **Warm Trust Threshold:** 0.2 (avg trust above which = warm dream)
   - **Shadow Trust Threshold:** -0.1 (avg trust below which = shadow dream)
5. **Dream tones:**
   - Warm: many friends or high trust → "夢を見た…温かかった" / "I dreamed... it was warm"
   - Shadow: negative trust dominates → "影の夢…" / "A dream of shadows..."
   - Water: neutral/peaceful → "水の夢…静かだった" / "A dream of water... quiet"
   - Void: no memories → "何もない夢…" / "An empty dream..."

### 2g-vi. AdaptivePersonality (optional)

1. Create an empty child GameObject under the NPC root named `AdaptivePersonality`
2. Add **UdonBehaviour**
3. Attach script: `AdaptivePersonality.cs`
4. Configure in Inspector:
   - **Session Memory:** drag the SessionMemory GameObject
   - **Belief State:** drag the BeliefState GameObject
   - **Manager:** drag the QuantumDharmaManager GameObject
   - **Update Interval:** 30 (seconds between personality evolution ticks)
   - **Change Rate:** 0.002 (very small per-tick change)
   - **Min Value / Max Value:** 0.1 / 0.9 (personality axis bounds)
   - **Starting Values:** Sociability 0.5, Cautiousness 0.5, Expressiveness 0.5
5. **Personality axes:**
   - Sociability: willingness to approach, trust growth rate modifier
   - Cautiousness: retreat sensitivity, threat response amplification
   - Expressiveness: speech frequency, emotion intensity, gesture amplitude
6. **Note:** Changes are very slow (designed for hours of interaction). The NPC's personality gradually evolves based on how it's treated.

### 2g-vii. IdleWaypoints (optional)

1. Create an empty child GameObject under the NPC root named `IdleWaypoints`
2. Add **UdonBehaviour**
3. Attach script: `IdleWaypoints.cs`
4. Create waypoint child Transforms:
   - Create 3-6 empty child GameObjects as waypoints (e.g., `Waypoint0`, `Waypoint1`, ...)
   - Position them around the NPC's home area (where it patrols when no players are nearby)
5. Configure in Inspector:
   - **Waypoints:** drag all waypoint Transforms into the array
   - **Min Pause Duration:** 5 (seconds minimum pause at each waypoint)
   - **Max Pause Duration:** 15 (seconds maximum pause at each waypoint)
   - **Arrival Distance:** 1.0 (meters from waypoint to consider arrived)
6. **Activation:** Only active during Silence state with no tracked players and not during dream cycle. The NPC walks sequentially between waypoints, pausing randomly at each one.

### 2g-viii. TrustVisualizer (optional)

1. Create an empty child GameObject under the NPC root named `TrustVisualizer`
2. Add **UdonBehaviour**
3. Attach script: `TrustVisualizer.cs`
4. Configure in Inspector:
   - **Markov Blanket:** drag the MarkovBlanket GameObject
   - **NPC:** drag the QuantumDharmaNPC GameObject
   - **Dream State:** (optional) drag the DreamState GameObject
   - **Belief State:** (optional) drag the BeliefState GameObject
   - **Manager:** (optional) drag the QuantumDharmaManager GameObject
   - **Renderers:** drag all NPC model Renderers into the array
   - **Trust Colors:** Low Trust (0.2, 0.25, 0.4), Neutral (0.5, 0.5, 0.55), High Trust (0.9, 0.75, 0.5)
   - **Emission Colors:** Warm (0.4, 0.3, 0.15), Friend (0.5, 0.4, 0.1), Anxious (0.3, 0.1, 0.1), Grateful (0.5, 0.35, 0.1)
   - **Dream Colors:** Dream (0.3, 0.25, 0.5), Emission Dream (0.2, 0.15, 0.4)
   - **Max Emission Intensity:** 0.5
   - **Color Smooth Speed:** 2 (per second)
   - **Dream Pulse Speed:** 1.5 (radians per second)
5. **Shader requirements:**
   - NPC material must support `_Color` and `_EmissionColor` properties
   - Standard Shader or any shader with these property names works
   - Uses MaterialPropertyBlock (no material instances created — Quest safe)

### 2g-ix. CuriosityDrive (optional)

1. Create an empty child GameObject under the NPC root named `CuriosityDrive`
2. Add **UdonBehaviour**
3. Attach script: `CuriosityDrive.cs`
4. Configure in Inspector:
   - **Manager:** drag the QuantumDharmaManager GameObject
   - **Belief State:** drag the BeliefState GameObject
   - **Session Memory:** drag the SessionMemory GameObject
   - **Free Energy Calculator:** (optional) drag the FreeEnergyCalculator GameObject
   - **First Meet Novelty:** 1.0 (high novelty for never-seen players)
   - **Reencounter Novelty:** 0.4 (moderate for remembered players)
   - **Friend Return Novelty:** 0.2 (low for returning friends — already familiar)
   - **Habituation Rate:** 0.02 (novelty decay per second)
   - **Novelty Floor:** 0.05 (minimum novelty — never fully habituated)
   - **Intent Surprise Boost:** 0.3 (novelty spike on intent change)
   - **Behavior PE Threshold:** 0.8 (PE above which novelty spikes)
   - **Behavior Surprise Boost:** 0.15 (novelty gain from high behavior PE)
   - **Curiosity Strength:** 0.5 (how much curiosity biases state selection)
   - **Update Interval:** 0.5 (seconds between updates)
5. **FEP interpretation:** Curiosity represents epistemic value — the NPC seeks information that will reduce model uncertainty. Novel stimuli lower the action cost threshold, making the NPC more likely to leave Silence and engage.

### 2g-x. GestureController (optional)

1. Create an empty child GameObject under the NPC root named `GestureController`
2. Add **UdonBehaviour**
3. Attach script: `GestureController.cs`
4. Configure in Inspector:
   - **Animator:** drag the Animator component on the Model GameObject
   - **Manager:** drag the QuantumDharmaManager GameObject
   - **NPC:** (optional) drag the QuantumDharmaNPC GameObject
   - **Belief State:** (optional) drag the BeliefState GameObject
   - **Adaptive Personality:** (optional) drag the AdaptivePersonality GameObject
   - **Animator Triggers:** `GestureWave`, `GestureBow`, `GestureHeadTilt`, `GestureNod`, `GestureBeckon`, `GestureFlinch` (defaults are fine)
   - **Global Cooldown:** 3 (seconds between any two gestures)
   - **Per Gesture Cooldown Multiplier:** 2 (multiplied by global cooldown per gesture)
   - **Idle Gesture Interval:** 8 (seconds between idle gesture checks)
   - **Idle Gesture Chance:** 0.3 (probability per check)
   - **Trust Thresholds:** Wave -0.2, Bow 0.3, Beckon 0.4, Nod 0.1
5. **Animator Controller setup:**
   - Create trigger parameters: `GestureWave`, `GestureBow`, `GestureHeadTilt`, `GestureNod`, `GestureBeckon`, `GestureFlinch`
   - Each trigger transitions to a corresponding gesture animation clip
   - Set transitions to "Has Exit Time" with appropriate clip length
   - Gestures should blend back to idle on exit
6. **Gesture types:**
   - WAVE: greeting on first Observe or idle during Approach
   - BOW: respect on gift receive or becoming Grateful
   - HEAD_TILT: curiosity on Observe or becoming Curious
   - NOD: acknowledgment on friendly intent or idle
   - BECKON: invitation to approach at high trust
   - FLINCH: startle on Retreat or becoming Anxious

### 2h. LookAtController (optional)

1. Create an empty child GameObject under the NPC root named `LookAtController`
2. Add **UdonBehaviour**
3. Attach script: `LookAtController.cs`
4. Configure in Inspector:
   - **Animator:** drag the Animator component on the Model GameObject
   - **Manager:** drag the QuantumDharmaManager GameObject
   - **NPC:** (optional) drag the QuantumDharmaNPC GameObject
   - **Eye Height Offset:** 1.5 (height of NPC eyes above pivot)
   - **Weight Transition Time:** 0.4 (seconds to ease gaze in/out)
5. **Important — Animator setup:**
   - Requires a **humanoid rig** (the script uses `GetBoneTransform(HumanBodyBones.Head/LeftEye/RightEye)`)
   - Gaze is applied via `LateUpdate` bone rotation (not IK Pass — UdonSharp has no `OnAnimatorIK`)
   - Set `Blink` parameter name to match your blend shape or animation parameter

### 2h-i. EmotionAnimator (optional)

1. Create an empty child GameObject under the NPC root named `EmotionAnimator`
2. Add **UdonBehaviour**
3. Attach script: `EmotionAnimator.cs`
4. Configure in Inspector:
   - **Animator:** drag the Animator component on the Model GameObject
   - **Manager:** drag the QuantumDharmaManager GameObject
   - **NPC:** (optional) drag the QuantumDharmaNPC GameObject
   - **Markov Blanket:** (optional) drag MarkovBlanket
   - **NPC Motor:** (optional) drag NPCMotor
   - **Crossfade Speed:** 3 (how fast emotion weights blend)
5. **Important — Animator Controller setup:**
   - Create float parameters: `EmotionCalm`, `EmotionCurious`, `EmotionWary`, `EmotionWarm`, `EmotionAfraid`
   - Create system parameters: `BreathAmplitude`, `NpcState`, `FreeEnergy`, `Trust`, `MotorSpeed`
   - Use a 2D or 1D Blend Tree to mix posture/gesture clips based on these parameters

### 2h-ii. SessionMemory (optional)

1. Create an empty child GameObject under the NPC root named `SessionMemory`
2. Add **UdonBehaviour**
3. Attach script: `SessionMemory.cs`
4. Configure in Inspector:
   - **Absent Trust Decay Rate:** 0.002 (trust loss per second while absent)
   - **Friend Trust Floor:** 0.3 (friends are never forgotten below this)
   - **Friend Trust Threshold / Kindness Threshold:** match BeliefState settings
   - **Decay Interval:** 5 (seconds between memory decay passes)
5. **VRChat networking:**
   - Set **Sync Mode** on the UdonBehaviour to **Manual**
   - SessionMemory uses `[UdonSynced]` arrays so all players see consistent NPC memory

### 2g-xi. GroupDynamics (optional)

1. Create an empty child GameObject under the NPC root named `GroupDynamics`
2. Add **UdonBehaviour**
3. Attach script: `GroupDynamics.cs`
4. Configure in Inspector:
   - **Player Sensor:** drag the PlayerSensor GameObject
   - **Belief State:** drag the BeliefState GameObject
   - **Group Radius:** 3.5 (max distance between players to form a group)
   - **Group Formation Time:** 2.0 (seconds clustered before group is recognized)
   - **Group Dissolution Time:** 3.0 (seconds apart before group dissolves)
   - **Friend Of Friend Factor:** 0.3 (trust transfer multiplier)
   - **Max Transfer Bonus:** 0.15 (cap on FoF trust bonus)
   - **Friend Trust Minimum:** 0.4 (friend must have this trust for transfer)
   - **Group Trust Smoothing:** 0.2 (EMA convergence rate)
   - **Update Interval:** 0.5
5. **FEP interpretation:** Groups reduce model complexity. When players cluster, the NPC can model them as a social entity. A friend's presence lowers the NPC's surprise at nearby strangers.

### 2g-xii. EmotionalContagion (optional)

1. Create an empty child GameObject under the NPC root named `EmotionalContagion`
2. Add **UdonBehaviour**
3. Attach script: `EmotionalContagion.cs`
4. Configure in Inspector:
   - **Belief State:** drag the BeliefState GameObject
   - **Free Energy Calculator:** (optional) drag the FreeEnergyCalculator GameObject
   - **Inertia Factor:** 0.05 (lower = more emotional inertia)
   - **Min Crowd Size:** 2 (minimum players for contagion to activate)
   - **Friendly Influence Weight:** 1.0
   - **Threat Influence Weight:** 1.5 (threatening players have outsized effect)
   - **Neutral Influence Weight:** 0.3
   - **Anxiety Growth Rate:** 0.04
   - **Anxiety Decay Rate:** 0.02
   - **Baseline Decay Rate:** 0.01
   - **Max Influence:** 0.6
   - **Update Interval:** 0.5
5. **How it works:** Estimates each player's "mood" from their dominant intent and behavior PE. Aggregates into a crowd valence [-1, 1]. The NPC's anxiety/warmth drifts slowly toward crowd mood with configurable inertia.

### 2g-xiii. AttentionSystem (optional)

1. Create an empty child GameObject under the NPC root named `AttentionSystem`
2. Add **UdonBehaviour**
3. Attach script: `AttentionSystem.cs`
4. Configure in Inspector:
   - **Belief State:** drag the BeliefState GameObject
   - **Free Energy Calculator:** (optional) drag the FreeEnergyCalculator GameObject
   - **Curiosity Drive:** (optional) drag the CuriosityDrive GameObject
   - **Threat Priority:** 4.0 (threats demand the most attention)
   - **Novelty Priority:** 2.5 (novel players attract attention)
   - **Friend Priority:** 1.5 (friends are predictable, need less)
   - **Approach Priority:** 2.0 (approaching players need monitoring)
   - **Neutral Priority:** 1.0
   - **Transition Speed:** 0.15 (smooth attention shifts)
   - **Free Energy Boost:** 0.5 (high FE demands attention)
   - **Update Interval:** 0.5
5. **FEP interpretation:** Attention IS precision in the free energy framework. The NPC allocates finite precision (confidence) across sensory channels based on expected information gain. `GetPrecisionMultiplier(slot)` returns [0.5, 2.0] for use by FreeEnergyCalculator.

### 2g-xiv. HabitFormation (optional)

1. Create an empty child GameObject under the NPC root named `HabitFormation`
2. Add **UdonBehaviour**
3. Attach script: `HabitFormation.cs`
4. Configure in Inspector:
   - **Player Sensor:** drag the PlayerSensor GameObject
   - **Belief State:** drag the BeliefState GameObject
   - **Habit Learning Rate:** 0.15 (per visit)
   - **Habit Decay Rate:** 0.005 (per check interval)
   - **Min Visits For Habit:** 3 (visits before habit is considered formed)
   - **Prediction Window Hours:** 1.5 (tolerance for "on schedule")
   - **Max Loneliness Signal:** 0.5
   - **Loneliness Build Rate:** 0.02
   - **Loneliness Decay Rate:** 0.1
   - **Update Interval:** 10 (seconds)
5. **How it works:** Tracks per-player visit timestamps in a 24-bin histogram. After enough visits, predicts when each player is expected. If a habitual visitor doesn't show up during their expected window, the NPC generates a loneliness signal that lowers the silence threshold — making the NPC restless.
6. **FEP interpretation:** This is temporal prediction error. The NPC's generative model now includes time as a dimension. "Player X usually arrives at hour H" is a temporal prior.

### 2g-xv. MultiNPCRelay (optional)

1. Create an empty child GameObject under the NPC root named `MultiNPCRelay`
2. Add **UdonBehaviour**
3. Attach script: `MultiNPCRelay.cs`
4. Configure in Inspector:
   - **Belief State:** drag the BeliefState GameObject
   - **Peer 0–3:** (optional) drag other NPC instances' MultiNPCRelay GameObjects
   - **Broadcast Trust Threshold:** 0.4 (minimum trust magnitude to broadcast)
   - **Broadcast Cooldown:** 5.0 (seconds between broadcasts)
   - **Receive Cooldown Per Player:** 10.0 (rate limit per player)
   - **Relay Trust Weight:** 0.25 (how much to trust relayed info)
   - **Max Prior Shift:** 0.15 (cap on trust adjustment from relay)
   - **Relay Decay Rate:** 0.005 (relayed info becomes stale)
   - **Check Interval:** 2.0 (seconds between broadcast checks)
5. **Multi-NPC setup:** For 2+ NPCs in the same world, wire each NPC's MultiNPCRelay as a peer of the others. NPC-A's relay goes into NPC-B's Peer 0 slot, and vice versa.
6. **FEP interpretation:** Hierarchical Bayesian inference across agents. Each NPC is an independent inference engine. Reputation relay acts as an empirical prior: "another agent has already observed this player."

### 2g-xvi. SharedRitual (optional)

1. Create an empty child GameObject under the NPC root named `SharedRitual`
2. Add **UdonBehaviour**
3. Attach script: `SharedRitual.cs`
4. Configure in Inspector:
   - **Ritual Locations:** drag up to 4 Transform markers for gathering points
   - **Ritual Radius:** 8.0 (distance from ritual location center for participation)
   - **Player Sensor:** drag the PlayerSensor GameObject
5. **Ritual location setup:**
   - Create 1-4 empty child GameObjects as ritual locations (e.g., `RitualPoint0`, `RitualPoint1`, ...)
   - Position them at meaningful gathering spots in the world (shrine, clearing, campfire)
   - The NPC detects when players gather at these points and may initiate shared behaviors

### 2g-xvii. CollectiveMemory (optional)

1. Create an empty child GameObject under the NPC root named `CollectiveMemory`
2. Add **UdonBehaviour**
3. Attach script: `CollectiveMemory.cs`
4. Configure in Inspector:
   - **Local Memory:** drag this NPC's own SessionMemory GameObject
   - **Peer Memory 0:** (optional) drag peer NPC's SessionMemory instance
   - **Peer Memory 1:** (optional) drag peer NPC's SessionMemory instance
   - **Peer Memory 2:** (optional) drag peer NPC's SessionMemory instance
   - **Peer Memory 3:** (optional) drag peer NPC's SessionMemory instance
5. **Multi-NPC setup:** For worlds with multiple NPCs, wire each peer NPC's SessionMemory into the Peer Memory slots. This allows the NPC to access shared relationship data across the collective.

### 2g-xviii. GiftEconomy (optional)

1. Create an empty child GameObject under the NPC root named `GiftEconomy`
2. Add **UdonBehaviour**
3. Attach script: `GiftEconomy.cs`
4. Configure in Inspector:
   - **Player Sensor:** drag the PlayerSensor GameObject
5. **FEP interpretation:** The gift economy tracks patterns of generosity and reciprocity across players. The NPC models giving behavior as a predictive signal — repeated gift-givers reduce prediction error, while the NPC may develop expectations around gift exchange patterns.

### 2g-xix. NormFormation (optional)

1. Create an empty child GameObject under the NPC root named `NormFormation`
2. Add **UdonBehaviour**
3. Attach script: `NormFormation.cs`
4. Configure in Inspector:
   - **Zone Transforms:** drag up to 8 Transform markers for behavioral zones
   - **Zone Radius:** 5.0 (radius of each behavioral zone)
   - **Player Sensor:** drag the PlayerSensor GameObject
5. **Zone setup:**
   - Create 1-8 empty child GameObjects as zone markers (e.g., `Zone0`, `Zone1`, ...)
   - Position them at areas where you want the NPC to track behavioral norms
   - The NPC learns what behavior is "normal" in each zone and adjusts expectations accordingly

### 2g-xx. OralHistory (optional)

1. Create an empty child GameObject under the NPC root named `OralHistory`
2. Add **UdonBehaviour**
3. Attach script: `OralHistory.cs`
4. Configure in Inspector:
   - **Habit Formation:** drag the HabitFormation GameObject
   - **Session Memory:** drag the SessionMemory GameObject
   - **NPC:** drag the QuantumDharmaNPC GameObject
5. **How it works:** OralHistory draws from visit patterns (HabitFormation) and relationship data (SessionMemory) to generate narrative accounts of the NPC's history. The NPC "remembers" significant events and can reference them in speech via QuantumDharmaNPC.

### 2g-xxi. NameGiving (optional)

1. Create an empty child GameObject under the NPC root named `NameGiving`
2. Add **UdonBehaviour**
3. Attach script: `NameGiving.cs`
4. Configure in Inspector:
   - **Habit Formation:** drag the HabitFormation GameObject
   - **Session Memory:** drag the SessionMemory GameObject
   - **Belief State:** drag the BeliefState GameObject
   - **Gift Receiver:** drag the GiftReceiver GameObject
5. **How it works:** The NPC assigns internal "names" (labels/archetypes) to players based on their behavioral patterns, relationship history, and gift-giving behavior. These names reflect the NPC's subjective experience of each player.

### 2g-xxii. Mythology (optional)

1. Create an empty child GameObject under the NPC root named `Mythology`
2. Add **UdonBehaviour**
3. Attach script: `Mythology.cs`
4. Configure in Inspector:
   - **Collective Memory:** drag the CollectiveMemory GameObject
   - **Name Giving:** drag the NameGiving GameObject
   - **NPC:** drag the QuantumDharmaNPC GameObject
5. **How it works:** Mythology synthesizes collective memory and player archetypes (from NameGiving) into narrative structures — recurring themes, "legends" about memorable players, and emergent world-lore. The NPC can reference these mythological elements in speech.

### 2i. NPCMotor

1. Add **UdonBehaviour**
2. Attach script: `NPCMotor.cs`
3. Configure in Inspector:
   - **Move Speed:** 1.5
   - **Rotation Speed:** 120
   - **Stopping Distance:** 1.5
   - **Max Roam Radius:** 20
   - **Player Sensor:** drag the PlayerSensor GameObject
4. **Important — VRChat networking:**
   - Add **VRC Object Sync** component to this GameObject
   - Set **Sync Mode** on the UdonBehaviour to **Continuous**
   - The NPCMotor uses `[UdonSynced]` variables for position/rotation

### 2j. QuantumDharmaManager

1. Add **UdonBehaviour**
2. Attach script: `QuantumDharmaManager.cs`
3. Wire required references in Inspector:
   - **Player Sensor:** drag the PlayerSensor GameObject
   - **Markov Blanket:** drag the MarkovBlanket GameObject
   - **NPC Motor:** drag the NPCMotor GameObject
4. Wire optional enhanced references:
   - **Free Energy Calculator:** (optional) drag FreeEnergyCalculator
   - **Belief State:** (optional) drag BeliefState
   - **NPC:** (optional) drag QuantumDharmaNPC
   - **Session Memory:** (optional) drag SessionMemory
   - **Touch Sensor:** (optional) drag TouchSensor
   - **Gift Receiver:** (optional) drag GiftReceiver
   - **Look At Controller:** (optional) drag LookAtController
   - **Emotion Animator:** (optional) drag EmotionAnimator
   - **Mirror Behavior:** (optional) drag MirrorBehavior
   - **Proximity Audio:** (optional) drag ProximityAudio
   - **Dream State:** (optional) drag DreamState
   - **Contextual Utterance:** (optional) drag ContextualUtterance
   - **Voice Detector:** (optional) drag VoiceDetector
   - **Dream Narrative:** (optional) drag DreamNarrative
   - **Adaptive Personality:** (optional) drag AdaptivePersonality
   - **Trust Visualizer:** (optional) drag TrustVisualizer
   - **Idle Waypoints:** (optional) drag IdleWaypoints
   - **Curiosity Drive:** (optional) drag CuriosityDrive
   - **Gesture Controller:** (optional) drag GestureController
   - **Group Dynamics:** (optional) drag GroupDynamics
   - **Emotional Contagion:** (optional) drag EmotionalContagion
   - **Attention System:** (optional) drag AttentionSystem
   - **Habit Formation:** (optional) drag HabitFormation
   - **Multi NPC Relay:** (optional) drag MultiNPCRelay
   - **Culture:**
   - **Shared Ritual:** (optional) drag SharedRitual
   - **Collective Memory:** (optional) drag CollectiveMemory
   - **Gift Economy:** (optional) drag GiftEconomy
   - **Norm Formation:** (optional) drag NormFormation
   - **Oral History:** (optional) drag OralHistory
   - **Mythology:**
   - **Name Giving:** (optional) drag NameGiving
   - **Mythology:** (optional) drag Mythology
5. Tune thresholds (defaults are good starting points):
   - **Comfortable Distance:** 4
   - **Approach Threshold:** 1.5
   - **Retreat Threshold:** 6.0
   - **Action Cost Threshold:** 0.5

### 2k. FreeEnergyVisualizer

1. Add a **LineRenderer** component
2. Add **UdonBehaviour**
3. Attach script: `FreeEnergyVisualizer.cs`
4. Wire references:
   - **Manager:** drag the QuantumDharmaManager GameObject
   - **Markov Blanket:** drag the MarkovBlanket GameObject
   - **Line Renderer:** drag this GameObject's LineRenderer
5. LineRenderer settings:
   - **Material:** assign an Unlit or Additive shader material
   - **Use World Space:** true (the script sets positions in world space)
   - Leave position count at 0 — the script manages it at runtime
6. Set **Visualizer Enabled** to true (or false to disable for Quest)

### 2l. DebugCanvas

1. Add **Canvas** component
   - **Render Mode:** World Space
   - **Width:** 0.4, **Height:** 0.3 (or adjust to taste)
   - Scale the Canvas transform to roughly 0.005 on all axes
2. Position the Canvas above the NPC's head (~2.2m up from root)
3. Inside the Canvas, create a **Panel** (`DebugPanel`):
   - Add an **Image** component for the state background
   - Add five **Text** children: StateLabel, FreeEnergyLabel, TrustLabel, RadiusLabel, DetailsLabel
   - Use a small font size (14–18) and monospace font if available
4. On the DebugPanel (or the Canvas root), add **UdonBehaviour**
5. Attach script: `DebugOverlay.cs`
6. Wire references:
   - **Manager:** drag QuantumDharmaManager
   - **Markov Blanket:** drag MarkovBlanket
   - **Player Sensor:** drag PlayerSensor
   - **NPC Motor:** drag NPCMotor
   - **Panel Root:** drag the DebugPanel GameObject
   - **State Label / FreeEnergy Label / Trust Label / Radius Label / Details Label:** drag each Text
   - **State Background:** drag the Panel's Image component
   - **Touch Sensor:** (optional) drag TouchSensor
   - **Gift Receiver:** (optional) drag GiftReceiver
   - **Dream State:** (optional) drag DreamState
   - **Mirror Behavior:** (optional) drag MirrorBehavior
   - **Contextual Utterance:** (optional) drag ContextualUtterance
   - **Proximity Audio:** (optional) drag ProximityAudio
   - **Voice Detector:** (optional) drag VoiceDetector
   - **Dream Narrative:** (optional) drag DreamNarrative
   - **Adaptive Personality:** (optional) drag AdaptivePersonality
   - **Trust Visualizer:** (optional) drag TrustVisualizer
   - **Idle Waypoints:** (optional) drag IdleWaypoints
   - **Curiosity Drive:** (optional) drag CuriosityDrive
   - **Gesture Controller:** (optional) drag GestureController
   - **Group Dynamics:** (optional) drag GroupDynamics
   - **Emotional Contagion:** (optional) drag EmotionalContagion
   - **Attention System:** (optional) drag AttentionSystem
   - **Habit Formation:** (optional) drag HabitFormation
   - **Multi NPC Relay:** (optional) drag MultiNPCRelay
   - **Shared Ritual:** (optional) drag SharedRitual
   - **Collective Memory:** (optional) drag CollectiveMemory
   - **Gift Economy:** (optional) drag GiftEconomy
   - **Norm Formation:** (optional) drag NormFormation
   - **Oral History:** (optional) drag OralHistory
   - **Name Giving:** (optional) drag NameGiving
   - **Mythology:** (optional) drag Mythology
7. Set **Start Visible** to false for production (true for testing)
8. To enable Interact toggle, add a **Box Collider** on the DebugPanel or NPC root

---

## 3. Required VRChat SDK Components

| Component | Where | Purpose |
|---|---|---|
| **VRC Object Sync** | NPCMotor GameObject | Syncs NPC position/rotation across network |
| **UdonBehaviour** | Each script GameObject | Runs UdonSharp scripts |
| **Box Collider** (trigger) | NPC root or DebugPanel | Enables VRChat Interact for debug toggle |
| **SphereCollider** (trigger) | TouchSensor GameObject | Detects player body entry for touch events |
| **VRC_Pickup** | Each gift pickup object | Enables grab/drop for gift detection |

**Note:** VRC Object Sync requires that the object has a Rigidbody. Add a **Rigidbody** with:
- **Is Kinematic:** true
- **Use Gravity:** false

---

## 4. Reference Wiring Summary

```
PlayerSensor
  ├─→ MarkovBlanket            (reads detection radius)
  ├─→ QuantumDharmaManager     (notifies on observation update)
  ├─→ HandProximityDetector    (optional: delegates hand queries)
  ├─→ PostureDetector          (optional: delegates posture queries)
  └─→ VoiceDetector            (optional: delegates voice/engagement queries)

HandProximityDetector
  └─→ PlayerSensor             (reads tracked players + distances)

PostureDetector
  └─→ PlayerSensor             (reads tracked players)

LookAtController
  ├─→ Animator                 (LateUpdate bone rotation for gaze — no IK Pass needed)
  ├─→ QuantumDharmaManager     (reads NPC state + focus player)
  └─→ QuantumDharmaNPC         (optional: reads emotion)

EmotionAnimator
  ├─→ Animator                 (drives blend tree parameters)
  ├─→ QuantumDharmaManager     (reads NPC state + FE)
  ├─→ QuantumDharmaNPC         (optional: reads emotion)
  ├─→ MarkovBlanket            (optional: reads trust)
  └─→ NPCMotor                 (optional: reads motor speed)

TouchSensor
  ├─→ MarkovBlanket            (reads trust for response modulation)
  └─→ NPC Transform            (reads NPC position + forward for zone classification)

GiftReceiver
  ├─→ NPC Transform            (reads NPC position for distance check)
  └─→ MarkovBlanket            (reads trust for context)

SessionMemory
  (no outgoing references — called by QuantumDharmaManager and DreamState)

DreamState
  ├─→ PlayerSensor             (reads tracked player count)
  └─→ SessionMemory            (calls DreamConsolidate for offline belief update)

MirrorBehavior
  ├─→ QuantumDharmaManager     (reads focus player + focus slot)
  ├─→ PostureDetector          (reads crouch ratio for mirroring)
  ├─→ PlayerSensor             (resolves focus player sensor index)
  └─→ BeliefState              (reads per-player trust for activation gating)

ContextualUtterance
  ├─→ QuantumDharmaNPC         (calls ForceDisplayText for contextual speech)
  ├─→ QuantumDharmaManager     (reads focus player + focus distance)
  ├─→ SessionMemory            (reads interaction time for long-presence detection)
  └─→ DreamState               (reads dream cycle state)

ProximityAudio
  ├─→ QuantumDharmaNPC         (reads current emotion)
  ├─→ QuantumDharmaManager     (reads focus distance)
  └─→ DreamState               (optional: reads dream state for audio modulation)

VoiceDetector
  └─→ PlayerSensor             (reads tracked players, distances, gaze, velocity)

DreamNarrative
  ├─→ DreamState               (reads dream duration)
  ├─→ SessionMemory            (reads memory count, friend count, avg trust)
  └─→ QuantumDharmaNPC         (calls ForceDisplayText for dream speech)

AdaptivePersonality
  ├─→ SessionMemory            (reads friend count + memory count)
  ├─→ BeliefState              (reads dominant intent for sampling)
  └─→ QuantumDharmaManager     (reads focus slot)

IdleWaypoints
  (no outgoing references — called by QuantumDharmaManager, uses NPCMotor)

TrustVisualizer
  ├─→ MarkovBlanket            (reads trust for color mapping)
  ├─→ QuantumDharmaNPC         (reads emotion for emission modulation)
  ├─→ DreamState               (optional: reads dream cycle for purple pulse)
  ├─→ BeliefState              (optional: reads per-player friend status)
  └─→ QuantumDharmaManager     (optional: reads focus slot)

CuriosityDrive
  ├─→ QuantumDharmaManager     (reads focus player + focus slot)
  ├─→ BeliefState              (reads posteriors + dominant intent for novelty tracking)
  ├─→ SessionMemory            (reads remembered/friend status for initial novelty)
  └─→ FreeEnergyCalculator     (optional: reads behavior PE for surprise spikes)

GestureController
  ├─→ Animator                 (fires trigger parameters for gesture animations)
  ├─→ QuantumDharmaManager     (reads NPC state + focus slot)
  ├─→ QuantumDharmaNPC         (reads current emotion for gesture selection)
  ├─→ BeliefState              (reads per-player trust for gesture gating)
  └─→ AdaptivePersonality      (optional: reads expressiveness for gesture frequency)

QuantumDharmaManager
  ├─→ PlayerSensor             (reads player observations + hand/crouch)
  ├─→ MarkovBlanket            (reads trust, sends trust adjustments)
  ├─→ NPCMotor                 (issues movement commands)
  ├─→ SessionMemory            (optional: save/restore player relationships)
  ├─→ TouchSensor              (optional: reads touch events + signals)
  ├─→ GiftReceiver             (optional: reads gift events + signals)
  ├─→ LookAtController         (optional: referenced for wiring)
  ├─→ EmotionAnimator          (optional: referenced for wiring)
  ├─→ DreamState               (optional: reads dream cycle, consumes wake events)
  ├─→ ContextualUtterance      (optional: notifies on player registration + dream wake)
  ├─→ MirrorBehavior           (optional: referenced for wiring)
  ├─→ ProximityAudio           (optional: referenced for wiring)
  ├─→ VoiceDetector            (optional: reads voice signal for belief update)
  ├─→ DreamNarrative           (optional: notifies on dream wake)
  ├─→ AdaptivePersonality      (optional: referenced for wiring)
  ├─→ TrustVisualizer          (optional: referenced for wiring)
  ├─→ IdleWaypoints            (optional: patrols during Silence with no players)
  ├─→ CuriosityDrive           (optional: reads curiosity bias for state selection)
  ├─→ GestureController        (optional: triggers gestures on gift/friend events)
  ├─→ GroupDynamics            (optional: reads FoF bonus for complexity cost)
  ├─→ EmotionalContagion       (optional: reads crowd anxiety for retreat threshold)
  ├─→ AttentionSystem          (optional: reads attention focus for gaze)
  ├─→ HabitFormation           (optional: reads loneliness for silence threshold, notifies arrival/departure)
  ├─→ MultiNPCRelay            (optional: broadcasts reputation on departure, reads prior shift on arrival)
  ├─→ SharedRitual             (optional: reads ritual gathering state)
  ├─→ CollectiveMemory         (optional: reads collective relationship data)
  ├─→ GiftEconomy              (optional: reads gift exchange patterns)
  ├─→ NormFormation            (optional: reads behavioral norms per zone)
  ├─→ OralHistory              (optional: reads narrative history for speech)
  ├─→ NameGiving               (optional: reads player archetypes)
  └─→ Mythology                (optional: reads mythological narratives for speech)

NPCMotor
  └─→ PlayerSensor             (reads closest player for convenience methods)

DebugOverlay
  ├─→ QuantumDharmaManager     (reads state, free energy, PE values)
  ├─→ MarkovBlanket            (reads trust, radius)
  ├─→ PlayerSensor             (reads tracked player count)
  ├─→ NPCMotor                 (reads motor state)
  ├─→ HandProximityDetector    (optional: reads hand proximity data)
  ├─→ PostureDetector          (optional: reads posture data)
  ├─→ SessionMemory            (optional: reads memory count + friend count)
  ├─→ LookAtController         (optional: reads gaze weight)
  ├─→ TouchSensor              (optional: reads touch state + zones)
  ├─→ GiftReceiver             (optional: reads gift count + signal)
  ├─→ DreamState               (optional: reads dream phase + duration)
  ├─→ MirrorBehavior           (optional: reads mirror state + intensity)
  ├─→ ContextualUtterance      (optional: reads last situation type)
  ├─→ ProximityAudio           (optional: reads audio volume + pitch)
  ├─→ VoiceDetector            (optional: reads voice/engagement signal)
  ├─→ DreamNarrative           (optional: reads dream tone + narrative text)
  ├─→ AdaptivePersonality      (optional: reads personality axes)
  ├─→ TrustVisualizer          (optional: reads emission intensity)
  ├─→ IdleWaypoints            (optional: reads patrol state + waypoint index)
  ├─→ CuriosityDrive           (optional: reads curiosity + novelty values)
  ├─→ GestureController        (optional: reads last gesture + cooldown)
  ├─→ GroupDynamics            (optional: reads group ID, size, trust, FoF bonus)
  ├─→ EmotionalContagion       (optional: reads crowd mood, anxiety, warmth)
  ├─→ AttentionSystem          (optional: reads attention focus, level, precision multiplier)
  ├─→ HabitFormation           (optional: reads habit count, loneliness, absent count)
  ├─→ MultiNPCRelay            (optional: reads relay count, peer count)
  ├─→ SharedRitual             (optional: reads ritual state + active count)
  ├─→ CollectiveMemory         (optional: reads collective memory entries)
  ├─→ GiftEconomy              (optional: reads gift exchange data)
  ├─→ NormFormation            (optional: reads norm strength + zone data)
  ├─→ OralHistory              (optional: reads history entries + narration state)
  ├─→ NameGiving               (optional: reads assigned names + archetype data)
  └─→ Mythology                (optional: reads mythology entries + active narrative)

GroupDynamics
  ├─→ PlayerSensor             (reads tracked player positions for clustering)
  └─→ BeliefState              (reads per-slot trust for group trust + FoF calculation)

EmotionalContagion
  ├─→ BeliefState              (reads dominant intent + posteriors for mood estimation)
  └─→ FreeEnergyCalculator     (optional: reads behavior PE for erratic detection)

AttentionSystem
  ├─→ BeliefState              (reads dominant intent for priority assignment)
  ├─→ FreeEnergyCalculator     (optional: reads per-slot FE for attention demand)
  └─→ CuriosityDrive           (optional: reads novelty for attention priority boost)

HabitFormation
  ├─→ PlayerSensor             (reads tracked players for presence checking)
  └─→ BeliefState              (reads slot data for player identification)

MultiNPCRelay
  ├─→ BeliefState              (reads per-slot trust for broadcast decisions)
  ├─→ Peer 0                   (optional: other NPC's MultiNPCRelay)
  ├─→ Peer 1                   (optional: other NPC's MultiNPCRelay)
  ├─→ Peer 2                   (optional: other NPC's MultiNPCRelay)
  └─→ Peer 3                   (optional: other NPC's MultiNPCRelay)

SharedRitual
  └─→ PlayerSensor             (reads tracked player positions for gathering detection)

CollectiveMemory
  ├─→ SessionMemory            (reads local NPC relationship data)
  ├─→ Peer Memory 0            (optional: peer NPC's SessionMemory)
  ├─→ Peer Memory 1            (optional: peer NPC's SessionMemory)
  ├─→ Peer Memory 2            (optional: peer NPC's SessionMemory)
  └─→ Peer Memory 3            (optional: peer NPC's SessionMemory)

GiftEconomy
  └─→ PlayerSensor             (reads tracked players for gift exchange tracking)

NormFormation
  └─→ PlayerSensor             (reads tracked player positions for zone behavior analysis)

OralHistory
  ├─→ HabitFormation           (reads visit patterns for historical narrative)
  ├─→ SessionMemory            (reads relationship data for historical narrative)
  └─→ QuantumDharmaNPC         (calls ForceDisplayText for history speech)

NameGiving
  ├─→ HabitFormation           (reads visit patterns for archetype assignment)
  ├─→ SessionMemory            (reads relationship history for naming)
  ├─→ BeliefState              (reads intent posteriors for behavioral classification)
  └─→ GiftReceiver             (reads gift history for naming)

Mythology
  ├─→ CollectiveMemory         (reads collective data for myth synthesis)
  ├─→ NameGiving               (reads player archetypes for narrative roles)
  └─→ QuantumDharmaNPC         (calls ForceDisplayText for mythological speech)

FreeEnergyVisualizer
  ├─→ QuantumDharmaManager     (reads normalized prediction error)
  └─→ MarkovBlanket            (reads current radius for ring size)
```

---

## 5. Testing Checklist

- [ ] Place NPC in scene, enter Play Mode
- [ ] Walk toward NPC — verify PlayerSensor detects you (check DebugOverlay)
- [ ] Observe state transitions: Silence → Observe → Approach as you approach gently
- [ ] Rush at NPC — verify Retreat state and trust decrease
- [ ] Stand still nearby looking at NPC — verify trust increases over time
- [ ] Walk away — verify return to Silence
- [ ] Extend hand toward NPC while keeping body distance — verify "Reach" indicator in debug overlay
- [ ] Crouch near NPC — verify "Crouch" indicator and trust growth acceleration
- [ ] Toggle DebugOverlay by clicking/interacting with the NPC
- [ ] Check FreeEnergyVisualizer ring matches blanket radius and pulses with PE
- [ ] Verify NPC gaze tracks your head when in Observe/Approach state
- [ ] During Retreat, verify NPC looks away with occasional glance-back
- [ ] In Silence, verify NPC slowly drifts gaze (idle look-around)
- [ ] Verify blink rate increases when free energy is high (nervous)
- [ ] Verify emotion blend weights crossfade smoothly in Animator
- [ ] Walk away and return — verify trust/kindness are restored from session memory
- [ ] Check "Mem" line in DebugOverlay shows memory count and friend status
- [ ] Walk into NPC's touch trigger (front) — verify hand zone detection + trust boost at high trust
- [ ] Walk into NPC's touch trigger at low trust — verify startle → brief Retreat
- [ ] Approach NPC from behind — verify back zone detection + trust penalty
- [ ] Reach hand above NPC head — verify head zone detection + comfort trust boost
- [ ] Stay in touch trigger > 1.5s — verify prolonged contact effect (escalating trust delta)
- [ ] Check touch cooldown: exit and re-enter quickly — verify no duplicate events
- [ ] Pick up a gift object, carry it to NPC, drop it — verify gift detection + trust boost
- [ ] First gift from a new player — verify outsized trust impact (surprise bonus)
- [ ] Drop a second gift — verify diminishing returns (habituation)
- [ ] On gift receive — verify NPC says "ありがとう" / "Thank you" (Grateful utterance)
- [ ] On gift receive — verify particle burst visual
- [ ] Check "Touch" and "Gifts" lines in DebugOverlay
- [ ] Walk away and return — verify gift count is restored from session memory
- [ ] Build & Test with 2 clients to verify network sync on NPCMotor and SessionMemory
- [ ] Walk away from NPC and wait 5+ seconds — verify NPC enters Drowsy → Dreaming phases
- [ ] While NPC is dreaming — verify dream particles appear with slow pulsing
- [ ] Return to dreaming NPC — verify Waking phase with ~1.5s disorientation
- [ ] After wake — verify contextual utterance: "...ん?" or "Waking up..."
- [ ] Return as a remembered player — verify "また会えた" or "We meet again"
- [ ] Return as a friend — verify "おかえり" or "Welcome back"
- [ ] First meeting with NPC — verify "はじめまして" or "Hello"
- [ ] Stay near NPC for 60+ seconds — verify long presence utterance: "ずっといてくれる" or "You stayed"
- [ ] Dream consolidation: leave NPC dreaming for a while → verify trust normalization and forgiveness in session memory
- [ ] Check "Dream:" line in DebugOverlay showing phase + duration
- [ ] Build trust above 0.5 and crouch near NPC — verify MirrorBehavior activates (NPC lowers)
- [ ] Stand up — verify NPC mirror posture smoothly returns to normal
- [ ] At low trust (<0.5) — verify MirrorBehavior stays OFF
- [ ] Check "Mirror:" line in DebugOverlay showing ON/OFF and drop/lean values
- [ ] Stand very close to NPC in calm state — verify soft ambient audio (humming)
- [ ] Trigger anxious state — verify pitch increases (faster breathing sound)
- [ ] Walk beyond 5m — verify audio fades to silence
- [ ] While NPC dreams — verify very quiet, slow-pitch dream audio
- [ ] Check "Audio:" line in DebugOverlay showing volume + pitch
- [ ] Stand still near NPC, face it — verify "Voice:" line shows engagement signal > 0
- [ ] Walk around quickly while near NPC — verify voice signal drops (movement reduces engagement)
- [ ] Face away from NPC while close — verify voice signal drops (gaze factor)
- [ ] After dream wake — verify delayed dream narrative appears (~3.5s after "...ん?")
- [ ] Dream with many friends → verify warm narrative ("夢を見た…温かかった")
- [ ] Dream with negative trust → verify shadow narrative ("影の夢…")
- [ ] Dream with no memories → verify void narrative ("何もない夢…")
- [ ] Check "DreamNarr:" line in DebugOverlay showing tone name
- [ ] Leave NPC alone (no players) in Silence — verify idle waypoint patrol begins
- [ ] Watch NPC walk between waypoints with random pauses
- [ ] Enter sensor range during patrol — verify patrol stops immediately
- [ ] Check "Patrol:" line in DebugOverlay showing Walk/Pause + waypoint index
- [ ] At low trust — verify NPC model has cool/dark colors
- [ ] Build trust up — verify NPC model shifts to warm golden colors with emission glow
- [ ] Become a friend — verify subtle persistent golden aura
- [ ] During dream — verify purple pulsing emission
- [ ] Check "TrustViz em:" line in DebugOverlay showing emission intensity
- [ ] After many friendly interactions — verify "Personality S:" increases in DebugOverlay
- [ ] After threat encounters — verify "Personality C:" increases
- [ ] After long peaceful existence — verify "Personality E:" increases
- [ ] Verify personality changes are very slow (hours of interaction, not minutes)
- [ ] First player in range — verify "Curiosity:" shows high novelty (~1.0 for new player)
- [ ] Stay near NPC — verify curiosity decays gradually (habituation)
- [ ] Return as remembered player — verify lower initial novelty (0.4)
- [ ] Return as friend — verify lowest initial novelty (0.2)
- [ ] Change behavior suddenly — verify novelty spikes (intent surprise boost)
- [ ] At high curiosity — verify NPC exits Silence more readily (lower action cost)
- [ ] Check "Curiosity:" line in DebugOverlay showing aggregate, focus, bias, tracked count
- [ ] Silence → Observe transition — verify NPC waves at player (if trust > -0.2)
- [ ] Entering Retreat — verify NPC flinches
- [ ] Observe → Approach at high trust — verify NPC beckons
- [ ] Emotion becomes Curious — verify head tilt gesture
- [ ] Emotion becomes Grateful — verify bow gesture
- [ ] Emotion becomes Warm — verify nod gesture
- [ ] Emotion becomes Anxious (not in Retreat) — verify flinch gesture
- [ ] During Observe/Approach — verify periodic idle gestures (head tilt, nod, wave, beckon)
- [ ] On gift receive — verify forced bow gesture (bypasses cooldown)
- [ ] Remembered friend returns — verify wave gesture
- [ ] High expressiveness — verify more frequent idle gestures
- [ ] Low expressiveness — verify less frequent idle gestures
- [ ] Check "Gesture:" line in DebugOverlay showing last gesture name + [CD] cooldown indicator
- [ ] Have 2+ players stand close together (within 3.5m) for 2+ seconds — verify "Groups:" shows 1
- [ ] One player is a friend, the other a stranger — verify FoF bonus appears in DebugOverlay
- [ ] Separate the group — verify group dissolves after 3 seconds apart
- [ ] Check "Groups:" line in DebugOverlay showing count, group ID, trust, size, FoF bonus
- [ ] Have 2+ calm/friendly players nearby — verify NPC warmth increases in "Crowd:" line
- [ ] Have erratic/threatening players nearby — verify NPC anxiety increases
- [ ] Single player — verify contagion does not activate (min crowd size = 2)
- [ ] All players leave — verify anxiety/warmth slowly decay to zero
- [ ] Check "Crowd:" line in DebugOverlay showing crowd size, mood, anxiety, warmth
- [ ] Multiple players in range — verify attention is distributed (not all to one player)
- [ ] Threatening player enters — verify they get highest attention priority
- [ ] Novel player enters — verify novelty boosts their attention
- [ ] Friend enters — verify they get moderate attention (predictable = less priority)
- [ ] Check "Attn:" line in DebugOverlay showing focus slot, slots, budget, level, precision
- [ ] Visit NPC 3+ times — verify habit forms (check "Habits:" line shows increasing count)
- [ ] Leave and return during expected window — verify calm greeting (low temporal PE)
- [ ] Don't return during expected window — verify loneliness signal builds
- [ ] Check "Habits:" line in DebugOverlay showing habit count, loneliness, absent count
- [ ] Place 2 NPCs in world, wire their MultiNPCRelay as peers
- [ ] Build trust with NPC-A, then approach NPC-B — verify NPC-B has slight trust prior
- [ ] Check "Relay:" line in DebugOverlay showing peer count and relay entries
- [ ] Place 2+ players at a ritual location (within 8m radius) — verify SharedRitual detects gathering
- [ ] Move players away from ritual location — verify ritual state deactivates
- [ ] Check "Ritual:" line in DebugOverlay showing active ritual + participant count
- [ ] With multiple NPCs wired as peers — verify CollectiveMemory aggregates relationship data across NPCs
- [ ] Check "CollMem:" line in DebugOverlay showing collective memory entries + peer count
- [ ] Give gifts to NPC repeatedly — verify GiftEconomy tracks gift exchange patterns
- [ ] Multiple players give gifts — verify gift economy distinguishes between givers
- [ ] Check "GiftEcon:" line in DebugOverlay showing gift exchange data
- [ ] Stand in a behavioral zone and act consistently — verify NormFormation learns expected behavior
- [ ] Violate a learned norm — verify NPC surprise increases (norm prediction error)
- [ ] Check "Norms:" line in DebugOverlay showing norm strength + zone data
- [ ] Visit NPC many times with established habits — verify OralHistory generates narrative references
- [ ] Check "History:" line in DebugOverlay showing history entries + narration state
- [ ] Build a relationship over multiple visits — verify NameGiving assigns an archetype
- [ ] Different behavior patterns — verify different archetypes are assigned
- [ ] Check "Names:" line in DebugOverlay showing assigned names + archetype data
- [ ] With CollectiveMemory and NameGiving active — verify Mythology synthesizes narrative elements
- [ ] Check "Myth:" line in DebugOverlay showing mythology entries + active narrative

---

## 6. Quest Optimization Notes

- Set **FreeEnergyVisualizer > Visualizer Enabled** to false on Quest builds
- Keep **DebugOverlay > Update Interval** at 0.2s or higher
- Keep **PlayerSensor > Poll Interval** at 0.25s or higher
- The LineRenderer material should use `VRChat/Mobile/Particles/Additive` or similar mobile shader
- Keep DebugCanvas text count minimal — each Text component has draw call cost
- DreamState particles: reduce emission rate (1–2) on Quest; use mobile-friendly particle material
- ProximityAudio: consider disabling on Quest if AudioSource performance is a concern
- MirrorBehavior: minimal performance impact (runs per frame but only does Lerp + transform set)
- VoiceDetector: poll interval 0.25s is fine; smoothing runs per frame but is lightweight (one Lerp per player)
- TrustVisualizer: uses MaterialPropertyBlock (no material instances) — safe for Quest
- IdleWaypoints: minimal performance (only active during Silence with no players)
- AdaptivePersonality: updates every 30s — negligible performance impact
- DreamNarrative: only active during wake transition — no ongoing cost
- CuriosityDrive: updates every 0.5s — lightweight (loops over 16 slots with simple math)
- GestureController: runs per frame but only does cooldown decrements (6 floats) + state checks; no heavy computation
- GroupDynamics: updates every 0.5s; O(n^2) pairwise distance check on MAX_SLOTS=16 (256 iterations max) — acceptable
- EmotionalContagion: updates every 0.5s; loops over 16 slots with simple mood classification — lightweight
- AttentionSystem: updates every 0.5s; normalization pass over 16 slots — negligible
- HabitFormation: updates every 10s; loops over 32 habit slots with histogram peak detection — very lightweight
- MultiNPCRelay: checks every 2s; iterates 4 peers max — negligible overhead
- SharedRitual: periodic check against up to 4 ritual locations with player distance checks — lightweight
- CollectiveMemory: reads from up to 4 peer SessionMemory instances on demand — no continuous tick cost
- GiftEconomy: event-driven (triggers on gift events); no per-frame cost when idle
- NormFormation: periodic zone checks against up to 8 zones; simple distance + behavior classification — lightweight
- OralHistory: generates narratives on demand from existing data — no ongoing tick cost
- NameGiving: event-driven archetype assignment; no per-frame cost when idle
- Mythology: synthesizes on demand from CollectiveMemory + NameGiving data — no continuous tick cost
