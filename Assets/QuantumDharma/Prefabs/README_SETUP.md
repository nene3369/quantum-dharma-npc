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
├── Model                     ← Your NPC mesh/avatar model
├── MarkovBlanket             ← Empty GameObject
├── PlayerSensor              ← Empty GameObject
├── HandProximityDetector     ← Empty GameObject (optional)
├── PostureDetector           ← Empty GameObject (optional)
├── NPCMotor                  ← Empty GameObject
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

### 2g. NPCMotor

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

### 2h. QuantumDharmaManager

1. Add **UdonBehaviour**
2. Attach script: `QuantumDharmaManager.cs`
3. Wire references in Inspector:
   - **Player Sensor:** drag the PlayerSensor GameObject
   - **Markov Blanket:** drag the MarkovBlanket GameObject
   - **NPC Motor:** drag the NPCMotor GameObject
4. Tune thresholds (defaults are good starting points):
   - **Comfortable Distance:** 4
   - **Approach Threshold:** 1.5
   - **Retreat Threshold:** 6.0
   - **Action Cost Threshold:** 0.5

### 2i. FreeEnergyVisualizer

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

### 2j. DebugCanvas

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
7. Set **Start Visible** to false for production (true for testing)
8. To enable Interact toggle, add a **Box Collider** on the DebugPanel or NPC root

---

## 3. Required VRChat SDK Components

| Component | Where | Purpose |
|---|---|---|
| **VRC Object Sync** | NPCMotor GameObject | Syncs NPC position/rotation across network |
| **UdonBehaviour** | Each script GameObject | Runs UdonSharp scripts |
| **Box Collider** (trigger) | NPC root or DebugPanel | Enables VRChat Interact for debug toggle |

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

QuantumDharmaManager
  ├─→ PlayerSensor             (reads player observations + hand/crouch)
  ├─→ MarkovBlanket            (reads trust, sends trust adjustments)
  └─→ NPCMotor                 (issues movement commands)

NPCMotor
  └─→ PlayerSensor             (reads closest player for convenience methods)

DebugOverlay
  ├─→ QuantumDharmaManager     (reads state, free energy, PE values)
  ├─→ MarkovBlanket            (reads trust, radius)
  ├─→ PlayerSensor             (reads tracked player count)
  ├─→ NPCMotor                 (reads motor state)
  ├─→ HandProximityDetector    (optional: reads hand proximity data)
  └─→ PostureDetector          (optional: reads posture data)

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
- [ ] Build & Test with 2 clients to verify network sync on NPCMotor

---

## 6. Quest Optimization Notes

- Set **FreeEnergyVisualizer > Visualizer Enabled** to false on Quest builds
- Keep **DebugOverlay > Update Interval** at 0.2s or higher
- Keep **PlayerSensor > Poll Interval** at 0.25s or higher
- The LineRenderer material should use `VRChat/Mobile/Particles/Additive` or similar mobile shader
- Keep DebugCanvas text count minimal — each Text component has draw call cost
