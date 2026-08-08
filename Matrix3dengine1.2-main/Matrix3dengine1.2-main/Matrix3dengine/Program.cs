#pragma warning disable CS4014 // Don't warn about unawaited async calls
#pragma warning disable CS0162 // Don't warn about unreachable code detects

using System;
using System.Threading;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Threading.Tasks;

class Program
{
    // Geometry Engine Storage (Original un-flipped coordinates)
    static float[][] blockVerts = new float[][] {
        new float[] {-1,-1,-1}, new float[] {1,-1,-1}, new float[] {1,1,-1}, new float[] {-1,1,-1},
        new float[] {-1,-1,1}, new float[] {1,-1,1}, new float[] {1,1,1}, new float[] {-1,1,1}
    };
    static int[][] blockEdges = new int[][] {
        new int[] {0,1}, new int[] {1,2}, new int[] {2,3}, new int[] {3,0},
        new int[] {4,5}, new int[] {5,6}, new int[] {6,7}, new int[] {7,4},
        new int[] {0,4}, new int[] {1,5}, new int[] {2,6}, new int[] {3,7}
    };

    // Spawn Point Environment Asset
    static float[][] spawnVerts = blockVerts;
    static int[][] spawnEdges = blockEdges;
    static bool hasCustomSpawn = false;

    // Camera / Engine Settings
    static float camX = 0, camY = 0f, camZ = -7f; 
    static float camRotX = 0, camRotY = 0;
    static int width = 80, height = 40;

    // Physics Variables 
    static float yVelocity = 0f;
    const float GRAVITY = -0.04f;   
    const float JUMP_FORCE = 0.35f; 
    const float GROUND_LEVEL = 0f;

    // Theme Colors 
    const string RESET = "\x1b[0m";
    const string BG_WHITE = "\x1b[47m";   
    const string FG_BLACK = "\x1b[30m";   
    const string FG_BLUE = "\x1b[34m";    
    const string FG_RED = "\x1b[31m";     
    const string FG_MAGENTA = "\x1b[35m"; 

    // Networking Variables
    static StreamWriter? networkWriter = null;
    static int myPlayerId = -1;
    static string myUsername = "Player";
    static Dictionary<int, float[]> remotePlayers = new Dictionary<int, float[]>();
    static Dictionary<int, string> playerNames = new Dictionary<int, string>();
    static Dictionary<int, string> playerMessages = new Dictionary<int, string>();
    static object networkLock = new object();

    static string currentLocalMessage = "";

    static void Main(string[] args)
    {
        Console.Clear();
        Console.WriteLine("=== MATRIX 3D MULTIPLAYER CANVAS ===");

        LoadSpawnEnvironmentObj();

        Console.Write("\nEnter your Username: ");
        myUsername = Console.ReadLine()?.Trim().Replace(",", "").Replace("|", "").Replace(":", "") ?? "Player";
        if (string.IsNullOrEmpty(myUsername)) myUsername = "Player";

        Console.Write("Type 'h' to HOST a public network server or 'j' to JOIN/BROWSE: ");
        string choice = Console.ReadLine()?.ToLower() ?? "h";

        if (choice == "h")
        {
            myPlayerId = 1;
            Task.Run(() => StartServerLoop());
            Task.Run(() => StartUdpBeaconLoop()); 
            ConnectToServer("127.0.0.1");
        }
        else
        {
            string targetConnection = DiscoverLocalServers();
            ConnectToServer(targetConnection);
        }

        Console.CursorVisible = false;
        Console.OutputEncoding = Encoding.UTF8;

        // Main Engine Loop
        while (true)
        {
            // Physics Engine Updates (Local camera jump tracking loop)
            if (camY > GROUND_LEVEL || yVelocity != 0f)
            {
                yVelocity += GRAVITY;
                camY += yVelocity;

                if (camY <= GROUND_LEVEL)
                {
                    camY = GROUND_LEVEL;
                    yVelocity = 0f;
                }
                SendNetworkUpdate();
            }

            if (Console.KeyAvailable)
            {
                ConsoleKey key = Console.ReadKey(true).Key;
                
                if (key == ConsoleKey.Enter)
                {
                    Console.SetCursorPosition(0, height + 1);
                    Console.Write(RESET + "Enter Chat or Command (/kill, /tp x y z):             ");
                    Console.SetCursorPosition(43, height + 1);
                    Console.CursorVisible = true;
                    string? input = Console.ReadLine()?.Trim();
                    Console.CursorVisible = false;
                    
                    if (!string.IsNullOrEmpty(input))
                    {
                        if (input.ToLower() == "/kill")
                        {
                            camX = 0; camY = 0f; camZ = -7f;
                            camRotX = 0; camRotY = 0;
                            yVelocity = 0f;
                            currentLocalMessage = "* Wasted *";
                        }
                        else if (input.ToLower().StartsWith("/tp "))
                        {
                            try
                            {
                                string[] parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                                if (parts.Length >= 4)
                                {
                                    camX = float.Parse(parts[1]);
                                    camY = float.Parse(parts[2]);
                                    camZ = float.Parse(parts[3]);
                                    yVelocity = 0f;
                                    currentLocalMessage = $"Teleported to {camX}, {camY}, {camZ}";
                                }
                            }
                            catch { currentLocalMessage = "Invalid /tp arguments!"; }
                        }
                        else
                        {
                            currentLocalMessage = input.Replace(",", " ").Replace("|", " ").Replace(":", " ");
                        }

                        string trackingMsg = currentLocalMessage;
                        Task.Run(async () => {
                            await Task.Delay(4000);
                            if (currentLocalMessage == trackingMsg) currentLocalMessage = "";
                            SendNetworkUpdate();
                        });
                    }
                    SendNetworkUpdate();
                }
                else
                {
                    float nextX = camX;
                    float nextZ = camZ;

                    float forwardX = MathF.Sin(camRotY);
                    float forwardZ = MathF.Cos(camRotY);
                    float rightX = MathF.Cos(camRotY);
                    float rightZ = -MathF.Sin(camRotY);

                    if (key == ConsoleKey.W) { nextX += forwardX * 0.4f; nextZ += forwardZ * 0.4f; }
                    if (key == ConsoleKey.S) { nextX -= forwardX * 0.4f; nextZ -= forwardZ * 0.4f; }
                    if (key == ConsoleKey.A) { nextX -= rightX * 0.4f; nextZ -= rightZ * 0.4f; }
                    if (key == ConsoleKey.D) { nextX += rightX * 0.4f; nextZ += rightZ * 0.4f; }

                    // Local player jump trigger
                    if (key == ConsoleKey.Spacebar && camY <= GROUND_LEVEL)
                    {
                        yVelocity = JUMP_FORCE;
                    }

                    // Map edge constraints
                    float distanceToCenter = MathF.Sqrt(nextX * nextX + nextZ * nextZ);
                    if (distanceToCenter > 1.8f) 
                    {
                        camX = nextX;
                        camZ = nextZ;
                    }
                    
                    if (key == ConsoleKey.LeftArrow)  camRotY -= 0.08f;
                    if (key == ConsoleKey.RightArrow) camRotY += 0.08f;
                    if (key == ConsoleKey.UpArrow)    camRotX -= 0.06f; 
                    if (key == ConsoleKey.DownArrow)  camRotX += 0.06f; 
                    
                    SendNetworkUpdate();
                }
            }

            char[,] charBuffer = new char[width, height];
            string[,] colorBuffer = new string[width, height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    charBuffer[x, y] = ' '; 
                    colorBuffer[x, y] = BG_WHITE + FG_BLACK;
                }
            }

            // --- SPAWN POINT MAP OBJECT ---
            RenderObject(spawnVerts, spawnEdges, 0f, 0f, 0f, charBuffer, colorBuffer, FG_BLUE, "[Spawn Point]");

            // --- REMOTE PLAYERS ---
            lock (networkLock)
            {
                foreach (var p in remotePlayers)
                {
                    if (p.Key == myPlayerId) continue;
                    float[] pos = p.Value;
                    
                    string nameTag = playerNames.ContainsKey(p.Key) ? playerNames[p.Key] : $"Player {p.Key}";
                    string msg = playerMessages.ContainsKey(p.Key) ? playerMessages[p.Key] : "";
                    string overhead = string.IsNullOrEmpty(msg) ? nameTag : $"{nameTag}: {msg}";
                    
                    RenderObject(blockVerts, blockEdges, pos[0], pos[1], pos[2], charBuffer, colorBuffer, FG_RED, overhead);
                }
            }

            string status = $"XYZ: {camX:0.0}, {camY:0.0}, {camZ:0.0} | Spawn Mesh: {(hasCustomSpawn ? "Custom OBJ" : "Box")} | Online: {remotePlayers.Count + 1}";
            for (int i = 0; i < status.Length && i < width; i++)
            {
                charBuffer[i, 0] = status[i];
                colorBuffer[i, 0] = BG_WHITE + FG_BLACK;
            }

            string prompt = "[Arrows = Look] | [WASD = Move] | [Space = Jump] | [Enter = Chat]";
            for (int i = 0; i < prompt.Length && i < width; i++) 
            {
                charBuffer[i, height - 1] = prompt[i];
                colorBuffer[i, height - 1] = BG_WHITE + FG_BLACK;
            }

            RenderToScreen(charBuffer, colorBuffer);
            Thread.Sleep(33);
        }
    }

    static bool Project3DPoint(float wx, float wy, float wz, out int sx, out int sy)
    {
        sx = 0; sy = 0;

        // Translation offsets relative to player camera coordinates
        float dx = wx - camX;
        
        // Multiplying wy by -1 keeps the object upside-down, while subtracting camY 
        // properly retains the standard screen translation behavior needed for jumping!
        float dy = (-wy) - camY;
        float dz = wz - camZ;

        // Horizontal Rotation Matrices
        float cosY = MathF.Cos(-camRotY);
        float sinY = MathF.Sin(-camRotY);
        float rx = dx * cosY + dz * sinY;
        float rz = -dx * sinY + dz * cosY;

        // Vertical Looking Pitch Matrix updates
        float cosX = MathF.Cos(-camRotX);
        float sinX = MathF.Sin(-camRotX);
        float ry = dy * cosX - rz * sinX;
        float finalZ = dy * sinX + rz * cosX;

        if (finalZ <= 0.5f) return false;

        // Perspective Matrix calculations mapped cleanly onto terminal screen
        float fovScale = 45.0f; 
        sx = (int)(width / 2 + (rx * fovScale / finalZ) * 2.2f); 
        
        // This minus sign handles standard top-down tracking so the screen reacts to jumping
        sy = (int)(height / 2 - (ry * fovScale / finalZ)); 

        return true;
    }

    static void RenderObject(float[][] verts, int[][] lines, float ox, float oy, float oz, char[,] cBuf, string[,] colBuf, string color, string overheadMsg)
    {
        int[][] proj = new int[verts.Length][];
        bool[] valid = new bool[verts.Length];
        float avgScreenX = 0;
        float highestY = 9999;
        int parsedPoints = 0;

        for (int i = 0; i < verts.Length; i++)
        {
            valid[i] = Project3DPoint(verts[i][0] + ox, verts[i][1] + oy, verts[i][2] + oz, out int sx, out int sy);
            if (valid[i])
            {
                proj[i] = new int[] { sx, sy };
                avgScreenX += sx;
                if (sy < highestY) highestY = sy;
                parsedPoints++;
            }
        }
        
        if (parsedPoints == 0) return;
        avgScreenX /= parsedPoints;

        foreach (var edge in lines)
        {
            if (edge[0] < verts.Length && edge[1] < verts.Length && valid[edge[0]] && valid[edge[1]])
            {
                DrawLine(proj[edge[0]][0], proj[edge[0]][1], proj[edge[1]][0], proj[edge[1]][1], cBuf, colBuf, color, '#');
            }
        }

        if (!string.IsNullOrEmpty(overheadMsg) && highestY != 9999)
        {
            int textX = (int)avgScreenX - (overheadMsg.Length / 2);
            int textY = (int)highestY - 2; 

            if (textY >= 0 && textY < height)
            {
                for (int i = 0; i < overheadMsg.Length; i++)
                {
                    int targetX = textX + i;
                    if (targetX >= 0 && targetX < width)
                    {
                        cBuf[targetX, textY] = overheadMsg[i];
                        colBuf[targetX, textY] = BG_WHITE + FG_MAGENTA; 
                    }
                }
            }
        }
    }

    static void DrawLine(int x0, int y0, int x1, int y1, char[,] cBuf, string[,] colBuf, string color, char character)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy, e2;
        while (true)
        {
            if (x0 >= 0 && x0 < width && y0 >= 0 && y0 < height) 
            { 
                cBuf[x0, y0] = character; 
                colBuf[x0, y0] = color; 
            }
            if (x0 == x1 && y0 == y1) break;
            e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    static void LoadSpawnEnvironmentObj()
    {
        string filePath = Path.Combine(Environment.CurrentDirectory, "custom.obj");
        Console.WriteLine($"[ENVIRONMENT] Checking project folder for spawn layout: {filePath}");

        if (!File.Exists(filePath))
        {
            Console.WriteLine("[ENVIRONMENT] No 'custom.obj' found. Spawn point will be a default cube.");
            return;
        }

        FileInfo info = new FileInfo(filePath);
        if (info.Length > 2 * 1024 * 1024) return;

        try
        {
            List<float[]> vertices = new List<float[]>();
            List<int[]> edges = new List<int[]>();
            HashSet<string> uniqueEdges = new HashSet<string>();

            foreach (string line in File.ReadLines(filePath))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("v "))
                {
                    string[] parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 4)
                    {
                        vertices.Add(new float[] {
                            float.Parse(parts[1]),
                            float.Parse(parts[2]),
                            float.Parse(parts[3])
                        });
                    }
                }
                else if (trimmed.StartsWith("f "))
                {
                    string[] parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    List<int> faceIndices = new List<int>();
                    for (int i = 1; i < parts.Length; i++)
                    {
                        string vertexPart = parts[i].Split('/')[0];
                        if (int.TryParse(vertexPart, out int idx)) faceIndices.Add(idx - 1);
                    }

                    for (int i = 0; i < faceIndices.Count; i++)
                    {
                        int v1 = faceIndices[i];
                        int v2 = faceIndices[(i + 1) % faceIndices.Count];
                        int min = Math.Min(v1, v2);
                        int max = Math.Max(v1, v2);
                        string key = $"{min}-{max}";
                        if (!uniqueEdges.Contains(key))
                        {
                            uniqueEdges.Add(key);
                            edges.Add(new int[] { v1, v2 });
                        }
                    }
                }
            }

            if (vertices.Count > 0 && edges.Count > 0)
            {
                spawnVerts = vertices.ToArray();
                spawnEdges = edges.ToArray();
                hasCustomSpawn = true;
                Console.WriteLine("[ENVIRONMENT] Success! Loaded custom.obj for center spawn point map structure.");
            }
        }
        catch 
        {
            spawnVerts = blockVerts;
            spawnEdges = blockEdges;
        }
    }

    static void SendNetworkUpdate()
    {
        try 
        { 
            string msgToSend = string.IsNullOrEmpty(currentLocalMessage) ? "NONE" : currentLocalMessage;
            networkWriter?.WriteLine($"{camX},{camY},{camZ},{myUsername},{msgToSend}"); 
        } 
        catch { }
    }

    static void StartUdpBeaconLoop()
    {
        using UdpClient udpServer = new UdpClient();
        udpServer.EnableBroadcast = true;
        IPEndPoint endPoint = new IPEndPoint(IPAddress.Parse("255.255.255.255"), 12346);

        while (true)
        {
            try
            {
                byte[] data = Encoding.UTF8.GetBytes($"3DSERVER:{myUsername}");
                udpServer.Send(data, data.Length, endPoint);
            }
            catch { }
            Thread.Sleep(1000);
        }
    }

    static string DiscoverLocalServers()
    {
        Console.WriteLine("\nScanning network for active game lobbies... (Waiting 3 seconds)");
        List<string> serverIps = new List<string>();
        List<string> serverNames = new List<string>();
        
        using UdpClient udpListener = new UdpClient(12346);
        udpListener.Client.ReceiveTimeout = 3000; 
        IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);

        DateTime endTime = DateTime.Now.AddSeconds(3);
        while (DateTime.Now < endTime)
        {
            try
            {
                byte[] receivedBytes = udpListener.Receive(ref remoteEP);
                string message = Encoding.UTF8.GetString(receivedBytes);
                
                if (message.StartsWith("3DSERVER:"))
                {
                    string hostName = message.Split(':')[1];
                    string ipAddress = remoteEP.Address.ToString();

                    if (!serverIps.Contains(ipAddress))
                    {
                        serverIps.Add(ipAddress);
                        serverNames.Add(hostName);
                        Console.WriteLine($"Found Server -> [{serverIps.Count}] Host: {hostName} (IP: {ipAddress})");
                    }
                }
            }
            catch (SocketException) { break; }
        }

        Console.WriteLine("\n=======================================================");
        Console.WriteLine("Options:");
        if (serverIps.Count > 0) Console.WriteLine("- Type the selection NUMBER of a discovered server.");
        Console.WriteLine("- OR type a manual IP Address / PC Hostname (.local)");
        Console.WriteLine("=======================================================");
        Console.Write("Input: ");
        
        string input = Console.ReadLine()?.Trim() ?? "";
        if (string.IsNullOrEmpty(input)) return "127.0.0.1";
        if (int.TryParse(input, out int selection) && selection > 0 && selection <= serverIps.Count) return serverIps[selection - 1];
        return input;
    }

    static void ConnectToServer(string targetHost)
    {
        try
        {
            IPAddress[] addresses = Dns.GetHostAddresses(targetHost);
            if (addresses.Length == 0) throw new Exception("Host not found");

            TcpClient client = new TcpClient();
            client.Connect(addresses[0], 12345);

            NetworkStream stream = client.GetStream();
            StreamReader reader = new StreamReader(stream);
            networkWriter = new StreamWriter(stream) { AutoFlush = true };

            Task.Run(() => {
                while (true)
                {
                    try
                    {
                        string? line = reader.ReadLine();
                        if (string.IsNullOrEmpty(line)) continue;

                        if (line.StartsWith("ID:"))
                        {
                            myPlayerId = int.Parse(line.Split(':')[1]);
                            SendNetworkUpdate(); 
                            continue;
                        }

                        lock (networkLock)
                        {
                            remotePlayers.Clear();
                            playerNames.Clear();
                            playerMessages.Clear();
                            string[] playerPackets = line.Split('|');
                            foreach (var player in playerPackets)
                            {
                                if (string.IsNullOrEmpty(player)) continue;
                                string[] parts = player.Split(':');
                                if (parts.Length < 2) continue; 

                                int id = int.Parse(parts[0]);
                                string[] data = parts[1].Split(',');
                                if (data.Length < 3) continue; 

                                remotePlayers[id] = new float[] {
                                    float.Parse(data[0]), float.Parse(data[1]), float.Parse(data[2])
                                };
                                
                                if (data.Length > 3) playerNames[id] = data[3];
                                if (data.Length > 4 && data[4] != "NONE") playerMessages[id] = data[4];
                            }
                        }
                    }
                    catch { break; }
                }
            });
        }
        catch
        {
            Console.WriteLine("\nConnection failed!");
            Console.ReadLine();
            Environment.Exit(0);
        }
    }

    static void StartServerLoop()
    {
        System.Collections.Concurrent.ConcurrentDictionary<int, string> serverPlayerPositions = new();
        System.Collections.Concurrent.ConcurrentDictionary<int, StreamWriter> serverWriters = new();
        int idCounter = 0;

        TcpListener listener = new TcpListener(IPAddress.Any, 12345);
        listener.Start();

        while (true)
        {
            TcpClient client = listener.AcceptTcpClient();
            int assignedId = Interlocked.Increment(ref idCounter);
            
            Task.Run(() => {
                using NetworkStream stream = client.GetStream();
                using StreamReader reader = new StreamReader(stream);
                using StreamWriter writer = new StreamWriter(stream) { AutoFlush = true };

                serverWriters[assignedId] = writer;
                serverPlayerPositions[assignedId] = "0,0,-7,Player,NONE";
                writer.WriteLine($"ID:{assignedId}");

                try
                {
                    while (!reader.EndOfStream)
                    {
                        string? msg = reader.ReadLine();
                        if (string.IsNullOrEmpty(msg)) continue;
                        serverPlayerPositions[assignedId] = msg;

                        List<string> packet = new();
                        var positionsSnapshot = serverPlayerPositions.ToArray();
                        foreach (var p in positionsSnapshot) packet.Add($"{p.Key}:{p.Value}");
                        string fullPacket = string.Join("|", packet);

                        var writersSnapshot = serverWriters.ToArray();
                        foreach (var w in writersSnapshot) { try { w.Value.WriteLine(fullPacket); } catch { } }
                    }
                }
                catch { }
                finally
                {
                    serverWriters.TryRemove(assignedId, out _);
                    serverPlayerPositions.TryRemove(assignedId, out _);
                }
            });
        }
    }

    static void RenderToScreen(char[,] cBuf, string[,] colBuf)
    {
        Console.SetCursorPosition(0, 0);
        StringBuilder sb = new StringBuilder();
        string activeColor = "";
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (colBuf[x, y] != activeColor) { activeColor = colBuf[x, y]; sb.Append(activeColor); }
                sb.Append(cBuf[x, y]);
            }
            if (y < height - 1) sb.Append('\n');
        }
        Console.Write(sb.ToString());
    }
}
