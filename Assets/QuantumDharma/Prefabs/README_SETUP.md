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
   - The NPC's Animator Controller must have **IK Pass** enabled on the relevant layer
   - Requires a humanoid rig or Animator with IK support
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
  └─→ PostureDetector          (optional: delegates posture queries)

HandProximityDetector
  └─→ PlayerSensor             (reads tracked players + distances)

PostureDetector
  └─→ PlayerSensor             (reads tracked players)

LookAtController
  ├─→ Animator                 (drives IK look-at)
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
  └─→ ProximityAudio           (optional: referenced for wiring)

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
  └─→ ProximityAudio           (optional: reads audio volume + pitch)

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
