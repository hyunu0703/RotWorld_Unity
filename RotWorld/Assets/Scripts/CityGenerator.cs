using System;
using System.Collections.Generic;
using UnityEngine;

namespace RotWorld
{
    /// <summary>
    /// 한 섬 안에 함께 배치되는 지역 유형
    /// </summary>
    public enum CityRegion : byte
    {
        Mixed,
        Villages,
        Downtown,
        Riverside,
        Industrial,
        Natural
    }

    /// <summary>
    /// 섬 도시 생성 입력과 허용 범위
    /// </summary>
    [Serializable]
    public sealed class CitySettings
    {
        // 외부 생성 요청에서 입력하는 인구 상한과 설정값
        public const int MaxActors = 10000;
        public int Size = 256;
        public int Seed = 12345;
        public int Density = 65;
        public int Humans;
        public int Zombies;
        public int Supplies = 100;

        // 외부 입력 범위 검사
        public void Validate()
        {
            // 생성창과 외부 입력의 지원 맵 크기 및 설정 범위 검증
            if ((Size != 128 &&
                Size != 256 &&
                Size != 512 &&
                Size != 1024) ||
                Density < 20 ||
                Density > 90 ||
                Humans < 0 ||
                Zombies < 0 ||
                Humans > MaxActors ||
                Zombies > MaxActors ||
                Supplies < 0 ||
                Supplies > 300)
            {
                throw new ArgumentException("맵 크기·밀도·인구·물자 설정 범위를 확인해 주세요");
            }
        }
    }

    /// <summary>
    /// 건물 영역과 출입구 및 실제 아이템 재고 보관
    /// </summary>
    public sealed class CityBuilding
    {
        // 생성된 건물의 위치와 출입구
        public RectInt Bounds { get; }
        public Vector2Int Door { get; }

        // 건물 용도와 아이템 재고
        public int Kind { get; }
        public int[] Stock { get; }

        // 생성된 필지와 재고 연결
        public CityBuilding(RectInt bounds, Vector2Int door, int kind, int[] stock)
        {
            // 생성 결과의 필지와 출입구 및 재고 참조 보관
            Bounds = bounds;
            Door = door;
            Kind = kind;
            Stock = stock;
        }
    }

    /// <summary>
    /// 지형과 건물 및 개체 배치 결과 보관
    /// </summary>
    public sealed class CityMap
    {
        // 지도 식별값과 타일별 지형 및 지역 데이터
        public int Size { get; }
        public int Seed { get; }
        public byte[] Tiles { get; }
        public byte[] Terrain { get; }
        public CityRegion[] Regions { get; }

        // 생활권 계획과 도로 연결 결과
        public List<Vector2Int> VillageCenters { get; } = new List<Vector2Int>();
        public List<RectInt> Bridges { get; } = new List<RectInt>();
        public float[] RegionTargets { get; } = new float[6];
        public List<List<Vector2Int>> RoadRoutes { get; } = new List<List<Vector2Int>>();

        // 건물 재고와 시작 개체 배치 결과
        public List<CityBuilding> Buildings { get; } = new List<CityBuilding>();
        public List<Vector2Int> Humans { get; } = new List<Vector2Int>();
        public List<Vector2Int> Zombies { get; } = new List<Vector2Int>();

        // 지도 배열 할당
        public CityMap(int size, int seed)
        {
            // 지도 크기와 시드 보관 및 타일별 배열 할당
            Size = size;
            Seed = seed;
            Tiles = new byte[size * size];
            Terrain = new byte[size * size];
            Regions = new CityRegion[size * size];
        }
    }

    /// <summary>
    /// 독립 난수 흐름으로 연결된 도로와 필지 및 개체 생성
    /// </summary>
    public static class CityGenerator
    {
        // 지도 배열에서 사용하는 타일 종류 식별자
        public const byte Grass = 0;
        public const byte Road = 1;
        public const byte Pavement = 2;
        public const byte Building = 3;
        public const byte Path = 4;
        public const byte Ocean = 5;
        public const byte Shallows = 6;
        public const byte Sand = 7;
        public const byte River = 8;
        public const byte Bridge = 9;

        // 섬 반경과 해안 전이 폭
        private const double IslandRadius = .42;
        private const int CoastWidth = 2;

        // 설정으로부터 지도 데이터 생성
        public static CityMap Generate(CitySettings settings)
        {
            // 입력 설정에서 지형과 시설 생성 결과 구성
            settings.Validate();
            var map = new CityMap(settings.Size, settings.Seed);
            GenerateIsland(map);
            new CityLayout(map, settings).Generate();

            // 도로 연결 검증 후 시작 개체 배치
            PlaceActors(map, settings);
            return map;
        }


        // 독립 시드로 만과 곶이 있는 섬 지형 생성
        private static void GenerateIsland(CityMap map)
        {
            // 지형 전용 난수에서 해안 굴곡과 섬 비율 계산
            var random = new System.Random(unchecked(map.Seed ^ 0x351A73));
            var phases = new double[5];

            for (int i = 0; i < phases.Length; i++)
            {
                phases[i] = random.NextDouble() * Math.PI * 2;
            }

            double stretch = .88 + random.NextDouble() * .16;

            for (int y = 0; y < map.Size; y++)
            {
                for (int x = 0; x < map.Size; x++)
                {
                    double nx = (x + .5 - map.Size * .5) / (map.Size * IslandRadius);
                    double ny = (y + .5 - map.Size * .5) / (map.Size * IslandRadius * stretch);
                    double angle = Math.Atan2(ny, nx);
                    double radius = .82 + .12 * Math.Sin(3 * angle + phases[0])
                        + .09 * Math.Sin(5 * angle + phases[1]) + .055 * Math.Sin(8 * angle + phases[2])
                        + .025 * Math.Sin(13 * angle + phases[3]) + .015 * Math.Sin(21 * angle + phases[4]);
                    map.Terrain[y * map.Size + x] = nx * nx + ny * ny < radius * radius ? Grass : Ocean;
                }
            }

            // 원래 육해 판정을 참조해 모래사장과 얕은 바다 추가
            Array.Copy(map.Terrain, map.Tiles, map.Terrain.Length);

            for (int y = CoastWidth; y < map.Size - CoastWidth; y++)
            {
                for (int x = CoastWidth; x < map.Size - CoastWidth; x++)
                {
                    int index = y * map.Size + x;
                    bool land = map.Tiles[index] == Grass;
                    bool coast = false;

                    for (int dy = -CoastWidth; dy <= CoastWidth && !coast; dy++)
                    {
                        for (int dx = -CoastWidth; dx <= CoastWidth; dx++)
                        {
                            if ((map.Tiles[(y + dy) * map.Size + x + dx] == Grass) != land)
                            {
                                coast = true;
                                break;
                            }
                        }
                    }

                    if (coast)
                    {
                        map.Terrain[index] = land ? Sand : Shallows;
                    }
                }
            }

            Array.Copy(map.Terrain, map.Tiles, map.Terrain.Length);
        }


        // 연결된 보행 공간 검증과 중복 없는 개체 배치
        private static void PlaceActors(CityMap map, CitySettings settings)
        {
            // 도로에서 연결된 공간을 순회할 방문 상태 준비
            var visited = new bool[map.Tiles.Length];
            var queue = new Queue<int>();
            int first = Array.FindIndex(map.Tiles, tile => tile == Road);

            if (first < 0)
            {
                throw new InvalidOperationException("연결된 도로가 없습니다");
            }

            queue.Enqueue(first);
            visited[first] = true;
            var available = new List<Vector2Int>();
            int[] dx =
            {
                -1,
                1,
                0,
                0
            };
            int[] dy =
            {
                0,
                0,
                -1,
                1
            };

            while (queue.Count > 0)
            {
                int index = queue.Dequeue();
                int x = index % map.Size;
                int y = index / map.Size;

                if (map.Tiles[index] == Pavement || map.Tiles[index] == Path)
                {
                    available.Add(new Vector2Int(x, y));
                }

                for (int d = 0; d < 4; d++)
                {
                    int nx = x + dx[d];
                    int ny = y + dy[d];

                    if (nx < 0 || ny < 0 || nx >= map.Size || ny >= map.Size)
                    {
                        continue;
                    }

                    int next = ny * map.Size + nx;

                    if (!visited[next] &&
                        map.Tiles[next] != Building &&
                        map.Tiles[next] != Ocean &&
                        map.Tiles[next] != Shallows &&
                        map.Tiles[next] != River)
                    {
                        visited[next] = true;
                        queue.Enqueue(next);
                    }
                }
            }

            // 생성된 모든 건물 출입구의 보행 연결 확인
            foreach (var building in map.Buildings)
            {
                if (!visited[building.Door.y * map.Size + building.Door.x])
                {
                    throw new InvalidOperationException("도로와 연결되지 않은 출입구가 있습니다");
                }
            }

            // 배치 공간을 넘는 요청은 중복 배치 없이 생성 중단
            if (settings.Humans + settings.Zombies > available.Count)
            {
                throw new ArgumentException($"시작 위치가 부족합니다 (최대 {available.Count}명) 인구를 줄여 주세요");
            }

            // 개체 전용 난수로 후보를 섞어 인간과 좀비 목록에 분배
            var random = new System.Random(unchecked(settings.Seed ^ 0x6B19E));

            for (int i = 0; i < settings.Humans + settings.Zombies; i++)
            {
                int pick = random.Next(i, available.Count);
                (available[i], available[pick]) = (available[pick], available[i]);

                if (i < settings.Humans)
                {
                    map.Humans.Add(available[i]);
                }
                else
                {
                    map.Zombies.Add(available[i]);
                }
            }
        }
    }
}
