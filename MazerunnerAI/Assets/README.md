# MazerunnerAI - ML-Agents Maze Chase Demo

## Project Overview
An ML-Agent (enemy) learns to chase a player through a randomly generated maze. Every training episode generates a new maze layout, forcing the agent to learn general navigation skills rather than memorizing paths.

- **Enemy wins**: catches the player within 60 seconds
- **Player wins**: survives without being caught for 60 seconds

---

## Project Structure

```
Assets/
├── Scripts/
│   ├── MazeGenerator.cs    — Random maze generation (recursive backtracking)
│   ├── EnemyAgent.cs       — ML-Agent that chases the player
│   ├── PlayerController.cs — Simple arrow-key player movement
│   └── GameManager.cs      — Game loop, timer, win/lose logic
├── Training/
│   └── maze_training.yaml  — ML-Agents training configuration
└── Scenes/
    └── SampleScene.unity   — Main scene
```

---

## Scene Setup Instructions

### Step 1: Create Prefabs

#### Wall Prefab
1. Create a **3D Object > Cube** in the scene
2. Name it `Wall`
3. Add a **Box Collider** (should already have one)
4. Give it a gray or dark material
5. Drag it into `Assets/Prefabs/` to create a prefab
6. Delete the instance from the scene

#### Floor Prefab
1. Create a **3D Object > Cube** in the scene
2. Name it `Floor`
3. Add a **Box Collider** (should already have one)
4. Give it a lighter material (e.g., white or light gray)
5. Drag it into `Assets/Prefabs/` to create a prefab
6. Delete the instance from the scene

### Step 2: Create the Game Manager

1. Create an **Empty GameObject**, name it `GameManager`
2. Add the **GameManager** script
3. Add the **MazeGenerator** script to the same object
4. Assign the **Wall** and **Floor** prefabs to the MazeGenerator fields

### Step 3: Create the Player

1. Create a **3D Object > Capsule**, name it `Player`
2. Set its **Tag** to `Player`
3. Add a **Rigidbody** component:
   - Uncheck "Use Gravity" or keep it on (the maze has floors)
   - Freeze Rotation X, Y, Z
4. Add the **PlayerController** script
5. Give it a blue or green material so it's visible

### Step 4: Create the Enemy (ML-Agent)

1. Create a **3D Object > Capsule**, name it `Enemy`
2. Add a **Rigidbody** component:
   - Freeze Rotation X, Y, Z
3. Add the **EnemyAgent** script
4. Add a **Behavior Parameters** component:
   - **Behavior Name**: `MazeChaser` (must match the YAML config)
   - **Vector Observation Size**: `12` (4 base observations + 8 wall rays)
   - **Continuous Actions**: `2` (move + rotate)
   - **Discrete Actions**: `0`
5. Add a **Decision Requester** component:
   - **Decision Period**: `5` (makes decisions every 5 steps)
6. Give it a red material so it's visible
7. Assign references:
   - Set `Player` as the player reference on EnemyAgent
   - Set `GameManager` as the gameManager reference on EnemyAgent

### Step 5: Connect References

On the **GameManager** component:
- Assign the **MazeGenerator** (same object)
- Assign the **Enemy** object
- Assign the **Player** object

### Step 6: Camera Setup

Position the **Main Camera** above the maze looking down:
- Position: `(10, 25, 10)` (adjust based on maze size)
- Rotation: `(90, 0, 0)`

### Step 7: UI (Optional)

1. Create a **Canvas** with two **TextMeshPro** texts
2. One for the timer, one for the result message
3. Assign them to the GameManager's UI fields

---

## How to Train

### Prerequisites
Install the ML-Agents Python package:
```bash
pip install mlagents
```

### Start Training
1. Open a terminal in the project root
2. Run:
```bash
mlagents-learn Assets/Training/maze_training.yaml --run-id=MazeChaser_01
```
3. When prompted, press **Play** in Unity

### Monitor Training
Open TensorBoard to see training progress:
```bash
tensorboard --logdir results
```

### Use Trained Model
1. After training, find the `.onnx` file in `results/MazeChaser_01/`
2. Drag it into the Unity project
3. On the Enemy's **Behavior Parameters**, set **Model** to the `.onnx` file
4. Set **Behavior Type** to `Inference Only`

---

## How the Agent Learns

### Observations (what the agent sees)
| # | Observation | Range | Purpose |
|---|-------------|-------|---------|
| 1 | Distance to player | 0-1 | Know how far the player is |
| 2 | Forward dot product | -1 to 1 | Is the player ahead or behind? |
| 3 | Right dot product | -1 to 1 | Is the player left or right? |
| 4 | Line of sight | 0 or 1 | Can the enemy see the player? |
| 5-12 | Wall ray distances | 0-1 each | Detect walls in 8 directions |

### Actions (what the agent does)
| # | Action | Range | Purpose |
|---|--------|-------|---------|
| 1 | Move | -1 to 1 | Forward/backward movement |
| 2 | Rotate | -1 to 1 | Left/right rotation |

### Rewards
| Event | Reward | Purpose |
|-------|--------|---------|
| Caught player | +1.0 | Main goal |
| Time ran out | -1.0 | Penalty for failing |
| Each step | -0.001 | Encourages speed |
| Getting closer | +0.002 | Guides toward player |
| Has line of sight | +0.005 | Encourages finding sight lines |

---

## Maze Settings

In the **MazeGenerator** component you can adjust:
- **Width / Height**: Number of cells (default: 7x7)
- **Cell Size**: World units per cell (default: 3)

Smaller mazes train faster. Start with 5x5 or 7x7 for initial training.
