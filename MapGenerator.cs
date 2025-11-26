using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections.Generic;

public class MapGenerator : MonoBehaviour
{
    [Header("Tilemap Settings")]
    public Tilemap tilemap;
    public RuleTile grassCliffTile;

    [Header("Map Settings")]
    public int width = 50;
    public int height = 50;

    [Range(0f, 1f)]
    public float noiseScale = 0.1f;
    [Range(0f, 1f)]
    public float threshold = 0.5f;

    [Header("Random Seed")]
    public int seed = 0;   // If 0 → automatically random

    private bool[,] landMap;

	void Start()
	{
		if (seed == 0)
			seed = Random.Range(100000, 999999);

		Debug.Log("Map Seed: " + seed);

		GenerateMap();
	}

	void GenerateMap()
	{
		float offsetX = seed * 0.12345f;
		float offsetY = seed * 0.54321f;

		// --- PHASE 1: Generate raw Perlin noise map ---
		landMap = new bool[width, height];
		for (int x = 0; x < width; x++)
		{
			for (int y = 0; y < height; y++)
			{
				float noise = Mathf.PerlinNoise(
					x * noiseScale + offsetX,
					y * noiseScale + offsetY
				);

				landMap[x, y] = noise > threshold;
			}
		}
		
		EnforceMinimumDistance(4);

		// --- PHASE 2: Remove protrusions ---
		RemoveProtrusions();

		// --- PHASE 3: Paint tiles with padding ---
		tilemap.ClearAllTiles();

		// ---- Add null padding around edges ----
		for (int x = -1; x <= width; x++)
		{
			tilemap.SetTile(new Vector3Int(x, -1, 0), null);
			tilemap.SetTile(new Vector3Int(x, height, 0), null);
		}
		for (int y = -1; y <= height; y++)
		{
			tilemap.SetTile(new Vector3Int(-1, y, 0), null);
			tilemap.SetTile(new Vector3Int(width, y, 0), null);
		}

		// ---- Paint internal map ----
		for (int x = 0; x < width; x++)
		{
			for (int y = 0; y < height; y++)
			{
				Vector3Int pos = new Vector3Int(x, y, 0);
				tilemap.SetTile(pos, landMap[x, y] ? grassCliffTile : null);
			}
		}
		
		RemoveProtrusions();

		tilemap.RefreshAllTiles();

		PrintMap();
	}
	
	void EnforceMinimumDistance(int minDistance)
	{
		int[,] regionId = new int[width, height];
		int currentId = 1;

		// --- STEP 1: Find all connected land regions ---
		for (int x = 0; x < width; x++)
		{
			for (int y = 0; y < height; y++)
			{
				if (landMap[x, y] && regionId[x, y] == 0)
				{
					// BFS flood-fill for region
					Queue<(int, int)> q = new Queue<(int, int)>();
					q.Enqueue((x, y));
					regionId[x, y] = currentId;

					while (q.Count > 0)
					{
						var (cx, cy) = q.Dequeue();

						foreach (var (dx, dy) in new (int, int)[] { (1,0),(-1,0),(0,1),(0,-1) })
						{
							int nx = cx + dx;
							int ny = cy + dy;

							if (nx >= 0 && ny >= 0 && nx < width && ny < height)
							{
								if (landMap[nx, ny] && regionId[nx, ny] == 0)
								{
									regionId[nx, ny] = currentId;
									q.Enqueue((nx, ny));
								}
							}
						}
					}
					currentId++;
				}
			}
		}

		// --- STEP 2: Force separation between regions ---
		bool[,] newMap = (bool[,])landMap.Clone();

		for (int x = 0; x < width; x++)
		{
			for (int y = 0; y < height; y++)
			{
				if (!landMap[x, y]) continue;

				int id = regionId[x, y];

				// Check neighbors within radius
				for (int dx = -minDistance; dx <= minDistance; dx++)
				{
					for (int dy = -minDistance; dy <= minDistance; dy++)
					{
						int nx = x + dx;
						int ny = y + dy;

						if (nx < 0 || ny < 0 || nx >= width || ny >= height)
							continue;

						if (!landMap[nx, ny]) continue;

						// if other region too close → remove this tile
						if (regionId[nx, ny] != id)
						{
							newMap[x, y] = false;
						}
					}
				}
			}
		}

		landMap = newMap;
	}
	
	void RemoveProtrusions()
	{
		Queue<(int x, int y)> queue = new Queue<(int, int)>();
		bool[,] queued = new bool[width, height]; // prevents duplicates

		// --- Enqueue all land tiles initially ---
		for (int x = 0; x < width; x++)
		{
			for (int y = 0; y < height; y++)
			{
				if (landMap[x, y])
				{
					queue.Enqueue((x, y));
					queued[x, y] = true;
				}
			}
		}

		// Directions for neighbors (8-way)
		(int dx, int dy)[] dirs = new (int, int)[]
		{
			(0, 1), (0, -1), (1, 0), (-1, 0),
			(1, 1), (-1, 1), (1, -1), (-1, -1)
		};

		// -------------------------------------
		// PROCESS QUEUE
		// -------------------------------------
		while (queue.Count > 0)
		{
			var (x, y) = queue.Dequeue();
			queued[x, y] = false;

			if (!landMap[x, y])
				continue; // already removed earlier

			// --- Collect neighbors ---
			bool C  = landMap[x, y];
			bool N  = (y + 1 < height) ? landMap[x, y + 1] : false;
			bool S  = (y - 1 >= 0)     ? landMap[x, y - 1] : false;
			bool E  = (x + 1 < width)  ? landMap[x + 1, y] : false;
			bool W  = (x - 1 >= 0)     ? landMap[x - 1, y] : false;

			bool NE = (x + 1 < width && y + 1 < height) ? landMap[x + 1, y + 1] : false;
			bool NW = (x - 1 >= 0 && y + 1 < height)     ? landMap[x - 1, y + 1] : false;
			bool SE = (x + 1 < width && y - 1 >= 0)      ? landMap[x + 1, y - 1] : false;
			bool SW = (x - 1 >= 0 && y - 1 >= 0)         ? landMap[x - 1, y - 1] : false;

			// ------------------------------
			// PATTERN CHECKS
			// ------------------------------

			bool remove = false;

			// Pattern 1
			if (C && E && !W && !N && !S && NE && !NW && !SW && SE)
				remove = true;

			// Pattern 2
			if (C && !E && W && !N && !S && !NE && !NW && SW && !SE)
				remove = true;

			// Pattern 3
			if (C && !E && W && !N && !S && !NE && NW && !SW && !SE)
				remove = true;

			// Pattern 4
			if (C && !E && !W && !N && S && !NE && !NW && SW && SE)
				remove = true;

			// Pattern 5
			if (C && !E && !W && N && !S && NE && NW && !SW && !SE)
				remove = true;

			// Pattern 6
			if (C && !E && !W && !N && S && NE && !NW && SW && SE)
				remove = true;

			// Pattern 7
			if (C && !E && !W && !N && S && !NE && !NW && SW && SE)
				remove = true;
			
			// Pattern 8
			if (C && !E && !W && N && S && !NE && !NW && !SW && !SE)
				remove = true;
			
			// Pattern 9
			if (C && !E && W && !N && !S && !NE && NW && SW && !SE)
				remove = true;
			
			// Pattern 10
			if (C && !E && !W && N && S && NE && !NW && SW && !SE)
				remove = true;
			
			// Pattern 11
			if (C && E && W && !N && !S && NE && !NW && SW && SE)
				remove = true;
			
			// Pattern 12
			if (C && !E && !W && N && S && !NE && NW && !SW && SE)
				remove = true;
			
			// Pattern 13
			if (C && E && W && !N && !S && NE && !NW && SW && !SE)
				remove = true;
			
			// Pattern 14
			if (C && !E && !W && N && S && NE && !NW && !SW && SE)
				remove = true;
			
			// Pattern 15
			if (C && E && W && !N && !S && !NE && NW && !SW && SE)
				remove = true;
			
			// Pattern 16
			if (C && E && W && !N && !S && NE && !NW && !SW && SE)
				remove = true;
			
			// Pattern 17
			if (C && !E && !W && N && S && !NE && NW && !SW && !SE)
				remove = true;		

			// Pattern 18
			if (C && !E && !W && N && S && !NE && !NW && SW && !SE)
				remove = true;		

			// Pattern 19
			if (C && E && W && !N && !S && !NE && NW && !SW && !SE)
				remove = true;	

			// Pattern 20
			if (C && E && W && !N && !S && NE && !NW && !SW && !SE)
				remove = true;		

			// Pattern 21
			if (C && E && W && !N && !S && !NE && !NW && SW && !SE)
				remove = true;	

			// Pattern 22
			if (C && E && W && !N && !S && !NE && !NW && !SW && SE)
				remove = true;						

			// Tiny cluster rule
			int neighborCount =
				(N ? 1 : 0) + (S ? 1 : 0) + (E ? 1 : 0) + (W ? 1 : 0) +
				(NE ? 1 : 0) + (NW ? 1 : 0) + (SE ? 1 : 0) + (SW ? 1 : 0);

			if (neighborCount <= 2)
				remove = true;

			// -------------------------------------
			// REMOVE TILE + enqueue neighbors
			// -------------------------------------
			if (remove)
			{
				landMap[x, y] = false;

				// All 8 neighbors need to be rechecked
				foreach (var (dx, dy) in dirs)
				{
					int nx = x + dx;
					int ny = y + dy;

					if (nx < 0 || ny < 0 || nx >= width || ny >= height)
						continue;

					if (!queued[nx, ny])
					{
						queue.Enqueue((nx, ny));
						queued[nx, ny] = true;
					}
				}
			}
		}
	}

	void PrintMap()
	{
		string mapString = "";
		for (int y = height - 1; y >= 0; y--)
		{
			for (int x = 0; x < width; x++)
			{
				mapString += landMap[x, y] ? "1 " : "0 ";
			}
			mapString += "\n";
		}
		Debug.Log(mapString);
	}
}
