# Matrix 3D Multiplayer Canvas + OBJ Engine

A lightweight, pure C# 3D wireframe graphics engine and multiplayer sandbox that runs entirely inside the native system terminal (Bash/Console). No heavy graphics APIs (OpenGL/DirectX) or external game engines required—just pure math, text buffers, and raw sockets.

## 🚀 Core Features

### 📡 Network & Discovery Architecture
* **Zero-Configuration Public Local Server Browser:** Uses **UDP Broadcasting** to scan the local network for active games. Players can view a live list of local lobbies and join with a single click.
* **Direct Connect Fallbacks:** Supports traditional connection methods via direct IP addresses or local hostnames (`.local`).
* **Multiplayer State Syncing:** Ultra-low latency TCP socket pipeline that syncs player positions, customized usernames, and live overhead chat bubbles in real-time.

### 📐 3D Software Renderer
* **Pure Text Buffer Canvas:** Renders 3D wireframes directly to a dynamic ANSI color-escaped character grid buffer.
* **Dynamic Camera Matrix:** Full First-Person camera orientation (`WASD` for relative movement, `Arrow Keys` to look around).
* **3D Perspective Projection:** Correctly handles 3D-to-2D viewport conversions, depth clipping, and wireframe line rasterization using Bresenham's algorithm.

### 📦 Dynamic `.obj` Asset Syncing Pipeline
* **Custom Model Parsing:** Built-in lightweight parser capable of importing real 3D Wavefront (`.obj`) models under 2MB (perfect for low-poly PS1/N64 era aesthetics).
* **On-the-Fly Asset Streaming:** When joining a host using a unique custom 3D avatar, the engine automatically checks file integrity via MD5 checksums. If a player doesn't have the model, a secure network tunnel streams the asset data directly across the TCP connection to sync avatars instantly.

---

## 🛠️ Built-in Environments & Scenes

When you boot up the engine, you are dropped into the default **Infinite Matrix Grid**:
* **The Spawn Point:** Features an interactive 3D focal node structure sitting at coordinates `(0, 0, 0)` so players can test spatial rendering and orientation instantly.
* **Horizon Plane:** A dynamic horizon boundary line that shifts relative to camera pitch and altitude to anchor player orientation.
* **Interactive Chat / Developer Commands:** Press `Enter` mid-game to safely stop rendering and execute built-in commands like `/tp x y z` to teleport across the infinite canvas or `/kill` to respawn at the origin.

## 🏃‍♂️ How to Run

1. Clone this repository.
2. Drop your favorite low-poly 3D wireframe model into the project directory and name it `custom.obj`.
3. Open your terminal and run:
   ```bash
   dotnet run
