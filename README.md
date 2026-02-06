# Quantum Dharma NPC

A VRChat AI NPC framework grounded in the **Free Energy Principle**. Instead of chasing rewards, this NPC minimizes prediction error against an internal generative model. It defaults to silence when action is costly, approaches when the world is predictable, and retreats when surprise is overwhelming. The result: an NPC whose optimal strategy — mathematically, thermodynamically — is kindness.

> **Kindness isn't soft. It's optimal.**
>
> In a system that minimizes free energy, trust expands the perceptual boundary,
> reduces prediction error, and lowers the cost of action. Aggression does the
> opposite — it contracts the boundary, increases surprise, and wastes energy.
> The physics don't prefer kindness because it's nice. They prefer it because
> it's efficient.

## Quick Start

### Requirements

- [Unity Hub](https://unity.com/download) + **Unity 2022.3 LTS**
- [VRChat Creator Companion (VCC)](https://vcc.docs.vrchat.com/)

### Setup

1. Clone this repo and add it to VCC as an existing project
2. VCC will install the VRChat Worlds SDK and UdonSharp automatically
3. Open the project in Unity via VCC
4. Follow the setup guide in [`Assets/QuantumDharma/Prefabs/README_SETUP.md`](Assets/QuantumDharma/Prefabs/README_SETUP.md) to wire up the NPC
5. **VRChat SDK > Show Control Panel > Build & Test**

## Architecture

```
                          ┌─────────────────────┐
                          │   VRChat Instance    │
                          │   (Player Events)    │
                          └──────────┬──────────┘
                                     │
                          ┌──────────▼──────────┐
                          │    PlayerSensor      │
                          │  (Perception Layer)  │
                          │  position, velocity, │
                          │  gaze, distance      │
                          └──────────┬──────────┘
                                     │ observations
               ┌─────────────────────▼─────────────────────┐
               │          QuantumDharmaManager              │
               │            (Core Orchestrator)             │
               │                                           │
               │  ┌──────────────────────────────────┐     │
               │  │  Free Energy Calculation          │     │
               │  │  F = Σ πᵢ · PEᵢ²                 │     │
               │  │  channels: distance, velocity,    │     │
               │  │           gaze                    │     │
               │  └──────────────┬───────────────────┘     │
               │                 │                         │
               │  ┌──────────────▼───────────────────┐     │
               │  │  State Selection                  │     │
               │  │  F < cost    → Silence (ground)   │     │
               │  │  F > retreat → Retreat             │     │
               │  │  F < approach & trust → Approach   │     │
               │  │  otherwise  → Observe              │     │
               │  └──────────────┬───────────────────┘     │
               └─────────────────┼─────────────────────────┘
                    ┌────────────┼────────────┐
                    │            │            │
          ┌─────────▼──┐  ┌─────▼──────┐  ┌──▼───────────┐
          │ NPCMotor   │  │  Markov    │  │ Debug / UI   │
          │ (Action)   │  │  Blanket   │  │              │
          │            │  │            │  │ DebugOverlay │
          │ walk to    │  │ trust →    │  │ FreeEnergy   │
          │ walk away  │  │ radius     │  │  Visualizer  │
          │ face       │  │ expand /   │  │              │
          │ stop       │  │ contract   │  │              │
          └────────────┘  └────────────┘  └──────────────┘
```

### Components

| Script | Layer | Role |
|---|---|---|
| `PlayerSensor` | Perception | Polls VRCPlayerApi for player position, velocity, gaze. No raycasts. |
| `MarkovBlanket` | Perception | Dynamic sensory boundary — expands with trust, contracts with threat. |
| `QuantumDharmaManager` | Core | Computes free energy, selects NPC state, issues motor commands and trust signals. |
| `NPCMotor` | Action | Smooth movement with owner-authoritative network sync. |
| `DebugOverlay` | UI | World-space panel showing state, F, trust, radius, PE breakdown. |
| `FreeEnergyVisualizer` | UI | LineRenderer ring that pulses with prediction error intensity. |

### NPC States

| State | Color | Condition | Motor |
|---|---|---|---|
| **Silence** | Green | F < action cost, or no players | Idle |
| **Observe** | Yellow | Moderate F, gathering information | Face player |
| **Approach** | Blue | Low F + sufficient trust | Walk toward |
| **Retreat** | Red | F > retreat threshold | Walk away |

## The Free Energy Principle

The NPC's behavior emerges from a single objective: **minimize variational free energy**.

**F = Σ πᵢ PEᵢ²**

Where:
- **PE** (prediction error) = divergence between what the NPC expects and what it observes
- **π** (precision) = confidence weighting on each sensory channel
- Three channels: distance from player, approach velocity, gaze direction

When free energy is low, the world is predictable — the NPC can act confidently. When free energy is high, the world is surprising — the NPC withdraws to protect its model. Trust modulates the **Markov blanket** (sensory boundary): kind players expand it, aggressive players contract it.

This is not a metaphor. It is the same mathematics that describes how biological organisms maintain homeostasis.

## Project Structure

```
Assets/QuantumDharma/
├── Scripts/
│   ├── Core/
│   │   └── QuantumDharmaManager.cs    # Central orchestrator
│   ├── Perception/
│   │   ├── PlayerSensor.cs            # Player detection & tracking
│   │   └── MarkovBlanket.cs           # Dynamic trust boundary
│   ├── Action/
│   │   └── NPCMotor.cs               # Movement controller
│   └── UI/
│       ├── DebugOverlay.cs            # World-space debug panel
│       └── FreeEnergyVisualizer.cs    # PE intensity ring effect
└── Prefabs/
    └── README_SETUP.md                # Inspector wiring guide
```

## Future Work

- **FreeEnergyCalculator** — Extract the F computation from the manager into a dedicated component for richer generative models
- **BeliefState** — Persistent NPC memory: posterior estimates of player intentions, updated via Bayesian inference
- **QuantumDharmaNPC** — Higher-level NPC personality layer: speech, animation triggers, emotional expression driven by belief state
- **Multi-NPC** — Multiple NPCs sharing observations and trust signals through a shared Markov blanket

## References

- Friston, K. (2010). *The free-energy principle: a unified brain theory?* Nature Reviews Neuroscience, 11(2), 127–138.
- *The Physics of Kindness* — the theoretical foundation for this framework: the thermodynamic argument that cooperative behavior is the minimum-energy solution in social systems.

## License

[MIT](LICENSE)
