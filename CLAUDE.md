# CLAUDE.md

## Project Overview

**quantum-dharma-npc** is a VRChat world project implementing the **Quantum Dharma Framework** — an AI NPC system grounded in the Free Energy Principle (FEP). Instead of maximizing rewards like conventional game AI, the NPC minimizes prediction error (free energy) against an internal generative model. When the cost of action exceeds its expected benefit, the NPC defaults to silence (inaction) as its thermodynamic ground state.

### Core Design Principles

- **Free Energy Minimization:** The NPC maintains a generative model of its environment and acts to reduce the divergence between predicted and observed states
- **Silence as Ground State:** Inaction is not failure — it is the default when action cost exceeds prediction error reduction. The NPC speaks or acts only when doing so meaningfully lowers free energy
- **Active Inference over Reward Maximization:** The NPC selects actions that confirm its predictions or update its model, not actions that maximize an external score

## Tech Stack

- **Engine:** Unity 2022.3 LTS
- **VRChat SDK:** VRChat Worlds SDK (UdonSharp)
- **Language:** UdonSharp (C#-like, compiles to Udon VM bytecode)
- **Platform:** VRChat (PC + Quest)
- **License:** MIT

## Architecture

```
                       ┌─────────────────────┐
                       │   VRChat Instance    │
                       │   (Player Events)    │
                       └──────────┬──────────┘
                                  │
               ┌──────────────────▼──────────────────┐
               │        Perception Layer (7)          │
               │                                      │
               │  PlayerSensor ─ HandProximityDetector │
               │  MarkovBlanket ─ PostureDetector      │
               │  TouchSensor ─ GiftReceiver           │
               │  VoiceDetector                        │
               └──────────────────┬──────────────────┘
                                  │ observations
               ┌──────────────────▼──────────────────┐
               │          Core Layer (25)              │
               │                                      │
               │  QuantumDharmaManager (orchestrator)  │
               │  FreeEnergyCalculator (5-ch PE)       │
               │  BeliefState (Bayesian inference)     │
               │  QuantumDharmaNPC (personality)       │
               │  SpeechOrchestrator (speech delegate) │
               │  SessionMemory (relationship persist) │
               │  DreamState (offline consolidation)   │
               │  DreamNarrative (wake speech)         │
               │  AdaptivePersonality (long-term evo)  │
               │  CuriosityDrive (epistemic value)     │
               │  ContextualUtterance (situation talk)  │
               │  GroupDynamics (cluster detection)     │
               │  EmotionalContagion (crowd mood)      │
               │  AttentionSystem (precision alloc)    │
               │  HabitFormation (temporal predict)    │
               │  MultiNPCRelay (NPC trust relay)      │
               │  SharedRitual (gathering rituals)     │
               │  CollectiveMemory (village memory)    │
               │  GiftEconomy (kindness chains)        │
               │  NormFormation (behavioral norms)     │
               │  OralHistory (story narration)        │
               │  NameGiving (player nicknames)        │
               │  Mythology (legend creation)          │
               │  CompanionMemory (co-presence)        │
               │  FarewellBehavior (goodbye rituals)   │
               └──────┬──────────┬──────────┬────────┘
                      │          │          │
               ┌──────▼──┐  ┌───▼──────┐  ┌▼────────────┐
               │ Action  │  │ Boundary │  │  UI Layer   │
               │ (7)     │  │          │  │  (3)        │
               │         │  │ Markov   │  │             │
               │ NPCMotor│  │ Blanket  │  │ DebugOverlay│
               │ LookAt  │  │          │  │ FreeEnergy  │
               │ Emotion │  │ trust →  │  │  Visualizer │
               │ Mirror  │  │ radius   │  │ Trust       │
               │ Gesture │  │          │  │  Visualizer │
               │ Prox    │  │          │  │             │
               │  Audio  │  │          │  │             │
               │ Idle    │  │          │  │             │
               │ Waypts  │  │          │  │             │
               └─────────┘  └──────────┘  └─────────────┘
```

### Data Flow

```
Perception ──observations──→ Manager ──motor commands──→ NPCMotor
                             Manager ──trust signals───→ MarkovBlanket
                             Manager ──belief updates──→ BeliefState
                             Manager ──FE computation──→ FreeEnergyCalculator
                             Manager ──emotion/speech──→ QuantumDharmaNPC
                             Manager ──speech tick─────→ SpeechOrchestrator
                             Manager ──save/restore────→ SessionMemory
                             Manager ──curiosity read──→ CuriosityDrive
                             Manager ──gesture fire────→ GestureController
                             Manager ──state/FE/PE─────→ DebugOverlay
                             Manager ──normalized PE───→ FreeEnergyVisualizer
MarkovBlanket ──radius──────→ PlayerSensor (detection range)
MarkovBlanket ──trust───────→ TrustVisualizer (color/glow)
DreamState ──consolidation──→ SessionMemory (offline belief update)
DreamNarrative ──wake text──→ QuantumDharmaNPC (ForceDisplayText)
ContextualUtterance ──text──→ QuantumDharmaNPC (ForceDisplayText)
GroupDynamics ──FoF bonus───→ Manager (complexity cost reduction)
EmotionalContagion ──mood───→ Manager (retreat/approach threshold shift)
AttentionSystem ──precision─→ FreeEnergyCalculator (per-slot multiplier)
HabitFormation ──loneliness─→ Manager (silence threshold shift)
MultiNPCRelay ──prior shift─→ BeliefState (trust prior on registration)
MultiNPCRelay ──reputation──→ Peer NPCs (cross-NPC trust relay)
SharedRitual ──ritual bonus──→ Manager (trust bonus for participants)
CollectiveMemory ──consensus──→ Mythology (legend detection)
CollectiveMemory ──village trust──→ Manager (initial trust bias)
GiftEconomy ──indirect karma──→ Manager (free energy reduction)
NormFormation ──violation──→ Manager (curiosity nudge)
OralHistory ──story text──→ QuantumDharmaNPC (ForceDisplayText)
NameGiving ──nickname──→ QuantumDharmaNPC (personalized greeting)
Mythology ──legend tale──→ QuantumDharmaNPC (ForceDisplayText)
CompanionMemory ──missing──→ SpeechOrchestrator (curiosity about absent companion)
FarewellBehavior ──farewell──→ QuantumDharmaNPC + GestureController (goodbye)
SpeechOrchestrator ──story──→ OralHistory (story telling trigger)
SpeechOrchestrator ──legend──→ Mythology (legend telling trigger)
SpeechOrchestrator ──norm───→ QuantumDharmaNPC (norm commentary speech)
SpeechOrchestrator ──trust──→ BeliefState (ritual/legend/collective trust adjustment)
Manager ──stage toggles──→ All components (enable/disable evolution stages)
SessionMemory ──emotion──→ Manager (peak emotion recall on re-encounter)
NameGiving ──nickname──→ QuantumDharmaNPC (personalized speech in TrySpeak)
```

### Component Inventory (42 scripts)

#### Perception Layer (7)

| Script | Sync Mode | Role |
|---|---|---|
| `PlayerSensor.cs` | None | Polls VRCPlayerApi, tracks position/velocity/gaze, delegates to sub-detectors |
| `MarkovBlanket.cs` | None | Dynamic trust boundary (radius expands/contracts with trust), editor gizmo |
| `HandProximityDetector.cs` | None | Detects reaching-out hand gestures via hand/body distance ratio |
| `PostureDetector.cs` | None | Detects player crouching via head-height ratio |
| `TouchSensor.cs` | None | Trigger-based touch zones (head/hand/back) with trust-modulated response |
| `GiftReceiver.cs` | None | Detects dropped VRC_Pickup objects as gifts, habituation model |
| `VoiceDetector.cs` | None | Behavioral engagement proxy (proximity + gaze + stillness) |

#### Core Layer (25)

| Script | Sync Mode | Role |
|---|---|---|
| `QuantumDharmaManager.cs` | None | Central orchestrator: state machine, adaptive decision tick, slot registration, named constants |
| `FreeEnergyCalculator.cs` | None | 5-channel PE: F = Σ(πᵢ_eff · PEᵢ²) - C with trust-modulated precision, slot eviction on overflow |
| `BeliefState.cs` | None | Bayesian intent inference (4 intents × 9 features), log-sum-exp stabilization, slot eviction |
| `QuantumDharmaNPC.cs` | Continuous | Personality: 5 emotions, 64-word tiered vocabulary, speech FIFO queue, breathing, particles |
| `SpeechOrchestrator.cs` | None | Delegates speech from Manager: stories, legends, norm commentary, companion signals, trust adjustments |
| `SessionMemory.cs` | Manual | Persistent player relationships (trust, kindness, friend, emotional memory) |
| `DreamState.cs` | None | Offline model consolidation during zero-player periods |
| `DreamNarrative.cs` | None | Generates contextual dream utterances on wake (4 tone types) |
| `AdaptivePersonality.cs` | None | Long-term personality evolution: sociability, cautiousness, expressiveness |
| `CuriosityDrive.cs` | None | Epistemic exploration: novelty-seeking, habituation, action cost bias |
| `ContextualUtterance.cs` | None | Situation-aware speech: first meet, re-encounter, friend return, long stay |
| `GroupDynamics.cs` | None | Spatial cluster detection, group trust, friend-of-friend transfer |
| `EmotionalContagion.cs` | None | Crowd mood estimation, NPC anxiety/warmth from aggregate player behavior |
| `AttentionSystem.cs` | None | Finite attention budget allocation, precision multiplier per slot |
| `HabitFormation.cs` | None | Visit pattern learning, temporal prediction, loneliness signal |
| `MultiNPCRelay.cs` | None | NPC-to-NPC reputation relay, Bayesian prior shift for new players |
| `SharedRitual.cs` | None | Temporal-spatial gathering rituals, trust bonus for participants |
| `CollectiveMemory.cs` | None | Village-level aggregated memories across NPCs |
| `GiftEconomy.cs` | None | Indirect kindness chains from gift-giving |
| `NormFormation.cs` | None | Location-based behavioral norm emergence |
| `OralHistory.cs` | None | Narrates accumulated memories as stories |
| `NameGiving.cs` | None | Internal nicknames for befriended players |
| `Mythology.cs` | None | Cross-NPC legend creation from collective memory |
| `CompanionMemory.cs` | None | Co-presence tracking, companion pair detection, FIFO missing companion signal queue |
| `FarewellBehavior.cs` | None | Trust-based farewell (glance/wave/emotional/friend), 24 bilingual utterances |

#### Action Layer (7)

| Script | Sync Mode | Role |
|---|---|---|
| `NPCMotor.cs` | Continuous | Owner-authoritative movement with synced position/rotation |
| `LookAtController.cs` | None | Head/eye gaze via LateUpdate bone rotation, saccades, blink system |
| `EmotionAnimator.cs` | None | Maps emotions to Animator blend tree parameters |
| `MirrorBehavior.cs` | None | Posture mirroring for trusted players (crouching, leaning) |
| `GestureController.cs` | None | Contextual gestures: wave, bow, head tilt, nod, beckon, flinch |
| `ProximityAudio.cs` | None | Emotion-driven spatial audio (volume/pitch per emotion) |
| `IdleWaypoints.cs` | None | Sequential waypoint patrol during Silence with no players |

#### UI Layer (3)

| Script | Sync Mode | Role |
|---|---|---|
| `DebugOverlay.cs` | None | World-space debug panel with 20+ telemetry lines, billboards to player |
| `FreeEnergyVisualizer.cs` | None | LineRenderer ring that pulses with prediction error |
| `TrustVisualizer.cs` | None | MaterialPropertyBlock-driven color/glow reflecting trust state |

## Project Structure

```
Assets/
├── QuantumDharma/
│   ├── Scripts/
│   │   ├── Core/                     # 25 scripts
│   │   │   ├── QuantumDharmaManager.cs
│   │   │   ├── FreeEnergyCalculator.cs
│   │   │   ├── BeliefState.cs
│   │   │   ├── QuantumDharmaNPC.cs
│   │   │   ├── SpeechOrchestrator.cs
│   │   │   ├── SessionMemory.cs
│   │   │   ├── DreamState.cs
│   │   │   ├── DreamNarrative.cs
│   │   │   ├── AdaptivePersonality.cs
│   │   │   ├── CuriosityDrive.cs
│   │   │   ├── ContextualUtterance.cs
│   │   │   ├── GroupDynamics.cs
│   │   │   ├── EmotionalContagion.cs
│   │   │   ├── AttentionSystem.cs
│   │   │   ├── HabitFormation.cs
│   │   │   ├── MultiNPCRelay.cs
│   │   │   ├── SharedRitual.cs
│   │   │   ├── CollectiveMemory.cs
│   │   │   ├── GiftEconomy.cs
│   │   │   ├── NormFormation.cs
│   │   │   ├── OralHistory.cs
│   │   │   ├── NameGiving.cs
│   │   │   ├── Mythology.cs
│   │   │   ├── CompanionMemory.cs
│   │   │   └── FarewellBehavior.cs
│   │   ├── Perception/               # 7 scripts
│   │   │   ├── PlayerSensor.cs
│   │   │   ├── MarkovBlanket.cs
│   │   │   ├── HandProximityDetector.cs
│   │   │   ├── PostureDetector.cs
│   │   │   ├── TouchSensor.cs
│   │   │   ├── GiftReceiver.cs
│   │   │   └── VoiceDetector.cs
│   │   ├── Action/                   # 7 scripts
│   │   │   ├── NPCMotor.cs
│   │   │   ├── LookAtController.cs
│   │   │   ├── EmotionAnimator.cs
│   │   │   ├── MirrorBehavior.cs
│   │   │   ├── GestureController.cs
│   │   │   ├── ProximityAudio.cs
│   │   │   └── IdleWaypoints.cs
│   │   └── UI/                       # 3 scripts
│   │       ├── DebugOverlay.cs
│   │       ├── FreeEnergyVisualizer.cs
│   │       └── TrustVisualizer.cs
│   ├── Tests/
│   │   └── Editor/                   # NUnit test suite
│   │       ├── FreeEnergyMathTests.cs
│   │       ├── BeliefUpdateMathTests.cs
│   │       └── QuantumDharma.Tests.Editor.asmdef
│   ├── Prefabs/
│   │   └── README_SETUP.md          # Inspector wiring guide (130+ test items)
│   ├── Animations/                   # Animator controllers and clips
│   ├── Materials/                    # NPC materials and shaders
│   └── Scenes/                       # Test world scenes
├── UdonSharp/                        # UdonSharp compiler (imported via VCC)
├── VRChat SDK/                       # VRChat Worlds SDK (imported via VCC)
Packages/                             # Unity Package Manager dependencies
ProjectSettings/                      # Unity project settings
```

## UdonSharp Constraints

UdonSharp is a strict subset of C#. Observe these limitations:

- **No generics** — use concrete types or arrays
- **No LINQ** — write explicit loops
- **No `dynamic`, `async/await`, `yield`** — all execution is synchronous per frame
- **No constructors on UdonSharpBehaviour** — use `Start()` or custom `Init()` methods
- **No runtime reflection** — type checks must be explicit
- **No delegates, events, or lambdas**
- **No string interpolation** (`$"..."`) — use string concatenation
- **No null-conditional operators** (`?.`, `??`) — use explicit null checks
- **Limited collection types** — use arrays (`T[]`), not `List<T>` or `Dictionary<K,V>`
- **No `OnAnimatorIK`** — use `LateUpdate` + `Animator.GetBoneTransform()` + `Quaternion.Slerp` for gaze/IK
- **`RequestSerialization()`** is Manual sync mode only — Continuous mode auto-syncs, calling it is a no-op/error
- **Synced variables** require `[UdonSynced]` attribute; Manual mode needs `RequestSerialization()`, Continuous does not
- **All public methods** on UdonSharpBehaviour are callable as Udon events
- **Network RPCs** use `SendCustomNetworkEvent(NetworkEventTarget, "MethodName")`
- **Pre-allocate arrays** in `Start()` — avoid `new T[]` in Update/tick loops (GC pressure)
- **`player.isLocal`** in trigger callbacks refers to the player entering, not the NPC object

## Coding Conventions

- PascalCase for public methods and properties
- camelCase with underscore prefix for private fields (e.g., `_freeEnergy`, `_beliefState`)
- One UdonSharpBehaviour per file, filename matches class name
- `[SerializeField]` for inspector-exposed private fields
- Keep `Update()` lightweight — offload heavy computation to periodic ticks
- Comment the FEP math: reference equations when implementing belief updates or precision weighting

## Key Concepts / Glossary

| Term | Meaning |
|---|---|
| Free Energy (F) | Upper bound on surprise; the quantity the NPC minimizes: F = Σ(πᵢ · PEᵢ²) - C |
| Generative Model | Internal model predicting sensory input given hidden states |
| Prediction Error (PE) | Divergence between expected and actual sensory input (5 channels) |
| Active Inference | Selecting actions to fulfill predictions (reduce PE) |
| Precision (π) | Confidence weighting on PE; trust-modulated (high trust → tolerant of closeness) |
| Complexity Cost (C) | Trust bonus that literally reduces F for identical observations |
| Ground State | Silence / inaction — the default when action cost > PE reduction |
| Belief State | Bayesian posterior over 4 intents: Approach, Neutral, Threat, Friendly |
| Kindness | Cumulative measure of friendly interactions; threshold for "friend" status |
| Markov Blanket | Trust-driven detection boundary (radius contracts/expands with trust) |
| Epistemic Value | Curiosity — the NPC seeks information to reduce model uncertainty |
| Dream Consolidation | Offline memory processing: trust normalization, forgiveness, friend boost |
| Session Memory | Persistent player data surviving dream cycles (trust, kindness, friend flag) |

## Build & Run

1. Install **Unity Hub** and **Unity 2022.3 LTS**
2. Install **VRChat Creator Companion (VCC)** and add this project
3. VCC manages SDK and UdonSharp packages — do not import them manually
4. Open the project in Unity via VCC
5. Test locally: **VRChat SDK > Show Control Panel > Build & Test**
6. Publish: **VRChat SDK > Show Control Panel > Build & Publish**

## Testing

- **Local testing:** Use VRChat SDK's "Build & Test" to launch a local instance
- **Multi-user testing:** "Build & Test" with "Number of Clients" > 1
- **Debug overlays:** Enable the in-world debug UI panel to inspect free energy, belief state, and action selection in real time
- **Unit tests:** 38 NUnit tests in `Assets/QuantumDharma/Tests/Editor/`:
  - `FreeEnergyMathTests.cs` (20 tests): ground state clamping, multi-channel PE, precision weighting, trust integration, normalization, division guards
  - `BeliefUpdateMathTests.cs` (18 tests): posterior normalization, Gaussian likelihood, Bayesian update, entropy/confidence, trust dynamics, intent history
  - Run via Unity Test Runner (Window > General > Test Runner > Edit Mode)
  - Tests use plain C# math wrappers since UdonSharp cannot run in Edit Mode directly

## Git Rules

- Always commit `.meta` files alongside their corresponding assets
- Never commit `Library/`, `Temp/`, `Obj/`, `Build/`, `UserSettings/`, or `Logs/`
- Prefab and scene merges are conflict-prone — coordinate before editing shared assets
