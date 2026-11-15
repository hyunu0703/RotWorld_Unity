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
    /// 건물 영역과 출입구 및 실제 아이템 재고와 정착지 저장 용량 보관
    /// </summary>
    public sealed class CityBuilding
    {
        // 생성된 건물의 위치와 출입구
        public RectInt Bounds { get; }
        public Vector2Int Door { get; }

        // 기존 외형 분류와 독립적인 건물 종류
        public int Kind { get; }
        public BuildingType Type { get; }
        public string Name => BuildingCatalog.GetName(Type);

        // ItemCatalog.All 순서의 아이템 재고와 재고·수색 표시 UI 갱신용 변경 버전
        private readonly int[] stock;
        public IReadOnlyList<int> Stock { get; }
        public int StockVersion { get; private set; }

        // 인간이 수색으로 찾아낸 아이템 종류별 개수
        private readonly int[] revealed;

        // 주거 시설의 음식·음료 저장 한도와 이 건물을 정착지로 둔 인간 수
        public int StorageCapacity { get; }
        public int SettlerCount { get; private set; }

        // 인간이 한 번이라도 들어가 수색을 시작했는지와 모든 재고를 찾아냈는지 여부
        public bool IsSearched { get; private set; }
        public bool IsSearchComplete { get; private set; }

        // 생성된 필지와 재고 및 저장 용량 연결
        public CityBuilding(RectInt bounds, Vector2Int door, int kind, int[] stock, BuildingType type = BuildingType.Unspecified,
            int storageCapacity = 0)
        {
            // 재고 배열 누락과 음수 용량 차단
            if (stock == null)
            {
                throw new ArgumentNullException(nameof(stock));
            }

            if (storageCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(storageCapacity));
            }

            // 생성 결과의 필지와 출입구 및 외부 수정 방지 재고 목록 보관
            Bounds = bounds;
            Door = door;
            Kind = kind;
            Type = type;
            this.stock = stock;
            Stock = Array.AsReadOnly(stock);
            revealed = new int[stock.Length];
            StorageCapacity = storageCapacity;
        }


        // 저장된 음식·음료 개수 조회
        public int StoredSupplyCount
        {
            get
            {
                // 가방 등 장비는 저장 용량 계산에서 제외
                int count = 0;

                for (int i = 0; i < stock.Length && i < ItemCatalog.All.Count; i++)
                {
                    if (ItemCatalog.All[i].IsSupply)
                    {
                        count += stock[i];
                    }
                }

                return count;
            }
        }


        // 수색으로 찾아낸 저장 음식·음료 개수 조회
        public int RevealedSupplyCount
        {
            get
            {
                // 남은 재고를 넘지 않는 발견 개수만 합산
                int count = 0;

                for (int i = 0; i < stock.Length && i < ItemCatalog.All.Count; i++)
                {
                    if (ItemCatalog.All[i].IsSupply)
                    {
                        count += GetRevealedCount(i);
                    }
                }

                return count;
            }
        }


        // 재고에 남은 아이템 총 개수 조회
        public int TotalStockCount
        {
            get
            {
                // 표시 여부 판단용으로 장비까지 합산
                int count = 0;

                foreach (int amount in stock)
                {
                    count += amount;
                }

                return count;
            }
        }


        // 수색으로 찾아낸 지정 아이템 개수 조회
        public int GetRevealedCount(int itemIndex)
        {
            // 섭취·파밍으로 줄어든 재고를 넘지 않게 제한
            if (itemIndex < 0 || itemIndex >= stock.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(itemIndex));
            }

            return Math.Min(revealed[itemIndex], stock[itemIndex]);
        }


        // 출입구만 바꾼 같은 건물 생성
        internal CityBuilding WithDoor(Vector2Int door)
        {
            // 출입 통로 확보 결과 반영 시 재고와 용량 유지
            return new CityBuilding(Bounds, door, Kind, stock, Type, StorageCapacity);
        }


        // 종류만 바꾼 같은 건물 생성
        internal CityBuilding WithType(BuildingType type)
        {
            // 종류 배정 결과 반영 시 재고와 용량 유지
            return new CityBuilding(Bounds, Door, Kind, stock, type, StorageCapacity);
        }


        // 수색으로 찾아낸 재고에서 아이템 한 개 차감
        internal bool TryTake(int itemIndex)
        {
            // 범위 밖 인덱스와 빈 재고 및 아직 찾지 못한 아이템 수집 차단
            if (itemIndex < 0 || itemIndex >= stock.Length || GetRevealedCount(itemIndex) <= 0)
            {
                return false;
            }

            // 찾아낸 아이템 한 개를 재고와 발견 개수에서 함께 빼고 UI에 알릴 버전 증가
            revealed[itemIndex] = GetRevealedCount(itemIndex) - 1;
            stock[itemIndex]--;
            StockVersion++;
            return true;
        }


        // 저장 용량 안에서 음식·음료 한 개 보관
        internal bool TryStore(int itemIndex)
        {
            // 범위 밖 인덱스와 음식·음료 외 아이템 및 용량 초과 차단
            if (itemIndex < 0 || itemIndex >= stock.Length || itemIndex >= ItemCatalog.All.Count ||
                !ItemCatalog.All[itemIndex].IsSupply || StoredSupplyCount >= StorageCapacity)
            {
                return false;
            }

            // 귀환 인간이 맡긴 아이템은 이미 아는 아이템으로 바로 표시하고 UI에 알릴 버전 증가
            stock[itemIndex]++;
            revealed[itemIndex]++;
            StockVersion++;
            return true;
        }


        // 인간이 들어가 수색을 시작한 건물로 기록
        internal void MarkSearched()
        {
            // 한 번 수색을 시작한 건물은 이후 계속 재고 표시 대상
            IsSearched = true;
        }


        // 수색 진행률에 맞춰 남은 아이템을 카탈로그 순서로 한 개씩 발견
        internal void RevealSearch(double progress)
        {
            // 진행률 1 이상이면 모든 재고, 그 전에는 진행률 비율만큼만 발견 목표 설정
            int total = TotalStockCount;
            int target = progress >= 1d ? total : (int)(total * Math.Max(0d, progress));
            int found = 0;
            bool isChanged = false;

            for (int i = 0; i < stock.Length; i++)
            {
                found += Math.Min(revealed[i], stock[i]);
            }

            // 아직 찾지 않은 첫 아이템부터 한 개씩 발견해 목록이 하나씩 늘어나게 처리
            for (int i = 0; i < stock.Length && found < target; i++)
            {
                while (revealed[i] < stock[i] && found < target)
                {
                    revealed[i]++;
                    found++;
                    isChanged = true;
                }
            }

            // 진행률을 채우면 수색 완료로 기록하고 변경이 있을 때만 UI 버전 증가
            if (progress >= 1d && !IsSearchComplete)
            {
                IsSearchComplete = true;
                isChanged = true;
            }

            if (isChanged)
            {
                StockVersion++;
            }
        }


        // 이 건물을 정착지로 확정한 인간 추가
        internal void AddSettler()
        {
            // 비축 목표 계산에 사용할 정착 인원 증가
            SettlerCount++;
        }


        // 이 건물을 정착지에서 해제한 인간 제거
        internal void RemoveSettler()
        {
            // 짝이 맞지 않는 해제 호출로 음수가 되는 상황 차단
            if (SettlerCount <= 0)
            {
                throw new InvalidOperationException("정착 인원 해제 호출 불일치");
            }

            SettlerCount--;
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
        // 감지와 추적에서 사용할 실제 이동 좌표와 구간 목적지
        // 근접 타격을 전달할 인간 이동 컴포넌트
        internal HashSet<int> InactiveHumans { get; } = new HashSet<int>();
        internal HashSet<int> ConvertedHumans { get; } = new HashSet<int>();
        internal HashSet<int> StarvedHumans { get; } = new HashSet<int>();
        internal Dictionary<int, UnitWander> ZombieUnits { get; } = new Dictionary<int, UnitWander>();
        internal HashSet<int> InactiveZombies { get; } = new HashSet<int>();
        public int HumanCount => Humans.Count - ConvertedHumans.Count - StarvedHumans.Count;

        // 건물 내부 인간 집합과 출입구 주변 조회
        public BuildingOccupancy Occupancy { get; }
        public int ZombieCount => Zombies.Count - InactiveZombies.Count;
        public event Action PopulationChanged;

        // 생성 시점별 생존 시간 진행
        internal HumanNeeds.Schedule NeedsSchedule { get; } = new HumanNeeds.Schedule();

        internal Dictionary<int, UnitWander> HumanUnits { get; } = new Dictionary<int, UnitWander>();
        internal Dictionary<int, Vector2> HumanPositions { get; } = new Dictionary<int, Vector2>();
        internal Dictionary<int, Vector2> ZombiePositions { get; } = new Dictionary<int, Vector2>();
        internal Dictionary<int, Vector2Int> HumanDestinations { get; } = new Dictionary<int, Vector2Int>();
        // 실제 좌표의 주변 충돌 후보 검색
        internal UnitSpatialIndex HumanSpatialIndex { get; } = new UnitSpatialIndex();
        internal UnitSpatialIndex ZombieSpatialIndex { get; } = new UnitSpatialIndex();

        // 전투와 협동의 실제 행동 좌표 및 추적 관계 검색
        internal UnitQueryIndex HumanQueries { get; }
        internal UnitQueryIndex ZombieQueries { get; }
        private readonly Dictionary<int, HashSet<int>> humanPursuers = new Dictionary<int, HashSet<int>>();
        private readonly Dictionary<int, Dictionary<int, int>> zombieDependents = new Dictionary<int, Dictionary<int, int>>();

        // 이동 예약 점유와 변환 공간 검색의 변경 상태
        internal HashSet<Vector2Int> OccupiedTiles { get; } = new HashSet<Vector2Int>();
        internal GridChangeTracker OccupancyChanges { get; }
        internal GridChangeTracker PositionChanges { get; }
        internal long OccupancyRevision => OccupancyChanges.Version;
        internal long PositionRevision => PositionChanges.Version;

        private readonly Dictionary<Vector2Int, int> occupancyCounts = new Dictionary<Vector2Int, int>();

        // 고정 식별자를 유지하는 인구 타일 기록
        public List<Vector2Int> Humans { get; } = new List<Vector2Int>();
        public List<Vector2Int> Zombies { get; } = new List<Vector2Int>();

        // 개체 등록과 기존 사전 순회 순위 갱신
        internal void RegisterUnit(bool zombie, int index, UnitWander unit)
        {
            // 같은 인덱스의 등록도 원본 사전과 행동 검색에 함께 반영
            (zombie ? ZombieUnits : HumanUnits)[index] = unit;
            (zombie ? ZombieQueries : HumanQueries).InvalidateOrder();
        }


        // 제거 개체의 행동 검색과 사전 등록 정리
        internal void RemoveUnit(bool zombie, int index)
        {
            // 이미 제거된 개체의 중복 순위 무효화 생략
            var queries = zombie ? ZombieQueries : HumanQueries;
            queries.Remove(index);

            if (zombie)
            {
                zombieDependents.Remove(index);
            }

            if ((zombie ? ZombieUnits : HumanUnits).Remove(index))
            {
                queries.InvalidateOrder();
            }
        }


        // 좀비의 기존 인간 추적 해제와 새 추적 관계 등록
        internal void ChangePursuer(int zombieIndex, int previousHuman, int nextHuman)
        {
            // 빈 추적 집합은 지도 수명 동안 재사용
            if (previousHuman >= 0 && humanPursuers.TryGetValue(previousHuman, out HashSet<int> previous))
            {
                previous.Remove(zombieIndex);
            }

            if (nextHuman < 0)
            {
                return;
            }

            if (!humanPursuers.TryGetValue(nextHuman, out HashSet<int> next))
            {
                next = new HashSet<int>();
                humanPursuers.Add(nextHuman, next);
            }

            next.Add(zombieIndex);
        }


        // 특정 인간을 추적하는 좀비 인덱스 수집
        internal void CollectPursuers(int humanIndex, List<int> results)
        {
            // 호출 중 상태 변경에 영향받지 않는 재사용 목록으로 전달
            results.Clear();

            if (humanPursuers.TryGetValue(humanIndex, out HashSet<int> pursuers))
            {
                foreach (int index in pursuers)
                {
                    if (ZombieUnits.ContainsKey(index))
                    {
                        results.Add(index);
                    }
                }
            }
        }


        // 인간의 도주와 공격 및 협동 관계별 좀비 참조 수 변경
        internal void ChangeZombieDependent(int humanIndex, int previousZombie, int nextZombie)
        {
            // 같은 대상 관계는 참조 수 유지
            if (previousZombie == nextZombie)
            {
                return;
            }

            if (previousZombie >= 0 && zombieDependents.TryGetValue(previousZombie, out Dictionary<int, int> previous) &&
                previous.TryGetValue(humanIndex, out int count))
            {
                if (count > 1)
                {
                    previous[humanIndex] = count - 1;
                }
                else
                {
                    previous.Remove(humanIndex);
                }
            }

            if (nextZombie < 0)
            {
                return;
            }

            if (!zombieDependents.TryGetValue(nextZombie, out Dictionary<int, int> next))
            {
                next = new Dictionary<int, int>();
                zombieDependents.Add(nextZombie, next);
            }

            next.TryGetValue(humanIndex, out int references);
            next[humanIndex] = references + 1;
        }


        // 쓰러진 좀비와 관계된 인간만 기존 통지 순서로 수집
        internal void CollectZombieDependents(int zombieIndex, List<int> results)
        {
            // 통지 중 관계 변경과 분리된 후보 스냅샷 구성
            results.Clear();

            if (zombieDependents.TryGetValue(zombieIndex, out Dictionary<int, int> dependents))
            {
                foreach (int index in dependents.Keys)
                {
                    if (HumanUnits.ContainsKey(index))
                    {
                        results.Add(index);
                    }
                }
            }

            HumanQueries.SortInOriginalOrder(results);
        }


        // 여러 유닛의 현재 위치와 이동 예약을 참조 수로 누적
        internal void OccupyTile(Vector2Int tile)
        {
            // 연속 추격이 다른 유닛의 예약 타일을 지날 때 기존 예약 보존
            occupancyCounts.TryGetValue(tile, out int count);
            occupancyCounts[tile] = count + 1;

            if (OccupiedTiles.Add(tile))
            {
                OccupancyChanges.Invalidate(tile);
            }
        }


        // 해당 유닛이 소유한 점유만 반환
        internal void ReleaseTile(Vector2Int tile)
        {
            // 다른 유닛의 현재 위치 또는 예약이 남은 타일은 점유 유지
            if (!occupancyCounts.TryGetValue(tile, out int count))
            {
                return;
            }

            if (count > 1)
            {
                occupancyCounts[tile] = count - 1;
                return;
            }

            occupancyCounts.Remove(tile);

            if (OccupiedTiles.Remove(tile))
            {
                OccupancyChanges.Invalidate(tile);
            }
        }


        // 공유 충돌 좌표와 해당 영역의 변경 번호 갱신
        internal void PublishUnitPosition(bool zombie, int index, Vector2 position)
        {
            // 프레임 끝 공유 좌표의 기존 등록 시점 유지
            var positions = zombie ? ZombiePositions : HumanPositions;

            if (positions.TryGetValue(index, out Vector2 previous))
            {
                if (previous.Equals(position))
                {
                    return;
                }

                PositionChanges.Invalidate(Vector2Int.FloorToInt(previous));
            }

            positions[index] = position;
            PositionChanges.Invalidate(Vector2Int.FloorToInt(position));
        }


        // 쓰러짐과 제거 시 기존 공유 좌표의 영역 무효화
        internal void RemoveUnitPosition(bool zombie, int index)
        {
            // 제거 전 좌표에서 정확한 변경 영역 조회
            var positions = zombie ? ZombiePositions : HumanPositions;

            if (positions.TryGetValue(index, out Vector2 previous))
            {
                positions.Remove(index);
                PositionChanges.Invalidate(Vector2Int.FloorToInt(previous));
            }
        }


        // 충돌과 변환 공간 검사에 사용되는 공유 좌표 변경 통지
        internal void NotifyPositionChanged()
        {
            // 프레임 안의 다른 개체 위치 변경도 즉시 캐시 무효화
            PositionChanges.InvalidateAll();
        }


        // 변환 후 인구 표시 갱신 통지
        internal void NotifyPopulationChanged()
        {
            // 변경된 인구 수를 UI 구독자에게 전달
            PopulationChanged?.Invoke();
        }


        // 지도 배열 할당
        public CityMap(int size, int seed)
        {
            // 지도 크기와 시드 보관 및 타일별 배열 할당
            Size = size;
            Seed = seed;
            Tiles = new byte[size * size];
            Terrain = new byte[size * size];
            Regions = new CityRegion[size * size];
            OccupancyChanges = new GridChangeTracker(size);
            PositionChanges = new GridChangeTracker(size);
            HumanQueries = new UnitQueryIndex(HumanUnits);
            ZombieQueries = new UnitQueryIndex(ZombieUnits);
            Occupancy = new BuildingOccupancy(this);
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
