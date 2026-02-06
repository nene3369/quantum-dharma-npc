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

## Project Structure

```
Assets/
├── QuantumDharma/
│   ├── Scripts/           # UdonSharp behaviours
│   │   ├── Core/          # Free energy engine, belief state, model update
│   │   ├── Perception/    # Sensory input processing (player proximity, gaze, events)
│   │   ├── Action/        # Action selection and motor output (animation, speech)
│   │   └── UI/            # Debug overlays, world-space UI panels
│   ├── Prefabs/           # NPC prefab, interaction zones, triggers
│   ├── Animations/        # Animator controllers and clips
│   ├── Materials/         # NPC materials and shaders
│   └── Scenes/            # Test world scenes
├── UdonSharp/             # UdonSharp compiler (imported via VCC)
├── VRChat SDK/            # VRChat Worlds SDK (imported via VCC)
Packages/                  # Unity Package Manager dependencies
ProjectSettings/           # Unity project settings
```

## UdonSharp Constraints

UdonSharp is a strict subset of C#. Observe these limitations:

- **No generics** — use concrete types or arrays
- **No LINQ** — write explicit loops
- **No `dynamic`, `async/await`, `yield`** — all execution is synchronous per frame
- **No constructors on UdonSharpBehaviour** — use `Start()` or custom `Init()` methods
- **No runtime reflection** — type checks must be explicit
- **Limited collection types** — use arrays (`T[]`), not `List<T>` or `Dictionary<K,V>`
- **Synced variables** require `[UdonSynced]` attribute and manual serialization calls (`RequestSerialization()`)
- **All public methods** on UdonSharpBehaviour are callable as Udon events
- **Network RPCs** use `SendCustomNetworkEvent(NetworkEventTarget, "MethodName")`

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
| Free Energy (F) | Upper bound on surprise; the quantity the NPC minimizes |
| Generative Model | Internal model predicting sensory input given hidden states |
| Prediction Error (PE) | Divergence between expected and actual sensory input |
| Active Inference | Selecting actions to fulfill predictions (reduce PE) |
| Precision (π) | Confidence weighting on prediction errors; low precision = ignore signal |
| Ground State | Silence / inaction — the default when action cost > PE reduction |
| Belief State | The NPC's posterior estimate of hidden world states |

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
- **Unit logic validation:** Core math (free energy calculation, belief update, precision weighting) should be testable in isolation via Unity Test Runner (NUnit) on plain C# wrappers where possible, since UdonSharp cannot run in Edit Mode tests directly

## Git Rules

- Always commit `.meta` files alongside their corresponding assets
- Never commit `Library/`, `Temp/`, `Obj/`, `Build/`, `UserSettings/`, or `Logs/`
- Prefab and scene merges are conflict-prone — coordinate before editing shared assets
