using System;
using UnityEngine;

namespace RotWorld
{
    /// <summary>
    /// 지형과 도로 아틀라스 참조 및 고정 지도 데이터 기반 픽셀 조회
    /// </summary>
    [CreateAssetMenu(menuName = "RotWorld/City Art Catalog")]
    public sealed class CityArtCatalog : ScriptableObject
    {
        // 4열 3행의 지형 전용 아틀라스 규격
        public const int PixelsPerTile = 16;
        private const int AtlasColumns = 4;
        private const int AtlasRows = 3;
        private const int SandStart = 4;
        private const int OceanStart = 6;
        private const int CoastStart = 8;
        private const int CoastDepth = 4;
        private const int CardinalDirectionCount = 4;
        private const int CornerDepth = 2;
        private const uint HorizontalHashMultiplier = 73856093u;
        private const uint VerticalHashMultiplier = 19349663u;
        private const uint HashAvalancheMultiplier = 0x7feb352du;

        // Inspector 지형 참조와 읽기 전용 원본 픽셀 캐시
        [SerializeField] private Texture2D terrainAtlas;
        private Color32[] atlasPixels;

        // 도로 표면과 연결부 및 표시 전용 차선 규격
        private const int RoadAtlasRows = 4;
        private const int PavementTile = 2;
        private const int EntranceTile = 3;
        private const int RoadEdgeStart = 4;
        private const int RoadCornerStart = 8;
        private const int HorizontalMarking = 12;
        private const int VerticalMarking = 13;
        private const int HorizontalBridge = 14;
        private const int VerticalBridge = 15;
        private const int RoadProbeRadius = 5;
        private const int MinimumLaneWidth = 4;
        private const int MaximumLaneWidth = 6;
        private const int MinimumDiagonalWidth = 3;
        private const int MaximumDiagonalWidth = 4;
        private const int DirectionDifference = 3;
        private const int DashPeriod = PixelsPerTile * 2;
        private const int DashLength = PixelsPerTile;
        private const int CurbDepth = 2;
        private const int BridgeRailDepth = 2;
        private const int BridgePostSpacing = 8;
        private const int HorizontalLane = 1;
        private const int VerticalLane = 2;
        private const int RisingLane = 3;
        private const int FallingLane = 4;

        // Inspector 도로 참조와 소형 원본 픽셀 캐시
        [SerializeField] private Texture2D roadAtlas;
        private Color32[] roadPixels;
        private static readonly Color32 CurbLight = new Color32(206, 204, 181, 255);
        private static readonly Color32 CurbShadow = new Color32(133, 135, 127, 255);

        // 공용 건물 조각과 머티리얼 및 지면 깊이 정렬 규격
        private const int BuildingAtlasRows = 19;
        private const int MaximumMapSize = 1024;
        private const int DepthStepsPerTile = 4;
        private const int DepthStride = 4;
        private const int FirstBuildingOrder = 8;
        public const int FactoryStackRise = 18;
        private static readonly Color32 White = new Color32(255, 255, 255, 255);
        private static readonly Color32[] ApartmentAccents =
        {
            new Color32(80, 130, 190, 255), new Color32(90, 160, 110, 255), new Color32(220, 140, 70, 255)
        };
        [SerializeField] private Texture2D buildingAtlas;
        [SerializeField] private Material buildingMaterial;
        private Color32[] buildingPixels;
        internal Material BuildingMaterial => buildingMaterial;

        /// <summary>
        /// 공용 건물 시트의 16픽셀 조각 식별자
        /// </summary>
        public enum BuildingPart
        {
            // 시트 0~31번 칸은 이전 조각 보존 영역이므로 32번부터 사용
            None = -1,
            Solid = 32, PlasterWall, BrickWall, HouseWindows,
            FishIcon, FrontDoor, RoofTiles, RoofEdge,
            BlockFence, Gate, YardPaving, Asphalt,
            Storefront, ShopDoor, Awning, SignBand,
            UpperWindow, FlatRoof, RoofTank, RoofUnit,
            MetalWall, ShutterBand, OfficeIcon, SawRoof,
            Smokestack, Container, ConvenienceIcon, MartIcon,
            PharmacyIcon, FoodIcon, CafeIcon, ToolIcon,
            // 고층과 공공 건물 간판과 외벽 및 옥상 조각
            HospitalIcon, PoliceIcon, CivicIcon, RestroomIcon,
            GuardIcon, HotelIcon, BagIcon, CurtainWall,
            Balcony, RoofCross, StoneWall
        }

        /// <summary>
        /// 건물 조합 규칙 선택용 정면 외형 형태
        /// </summary>
        public enum BuildingForm
        {
            House, Villa, Shop, Factory, Warehouse,
            Apartment, Office, Hotel, Mart, DepartmentStore,
            Hospital, Police, Civic, Restroom, Outpost
        }

        /// <summary>
        /// 지도 생성 데이터와 분리한 건물 형태와 높이 및 색상 선택 결과
        /// </summary>
        public readonly struct BuildingStyle
        {
            // 정면 시점 형태와 필지 위로 올라가는 표시 높이
            public BuildingForm Form { get; }
            public int Height { get; }

            // 색상 변형과 업종 간판 및 차양 선택
            public int Variation { get; }
            public BuildingPart Sign { get; }
            public Color32 Accent { get; }
            public bool HasAwning { get; }

            // 건물의 읽기 전용 외형 선택값 보관
            internal BuildingStyle(BuildingForm form, int height, int variation, BuildingPart sign, Color32 accent, bool hasAwning)
            {
                // 도시 생성 난수와 독립적으로 결정된 표시 설정 전달
                Form = form;
                Height = height;
                Variation = variation;
                Sign = sign;
                Accent = accent;
                HasAwning = hasAwning;
            }
        }

        /// <summary>
        /// 구역 합성 중 타일당 한 번 조회하는 도로 표시 입력
        /// </summary>
        public readonly struct StreetTile
        {
            // 최종 타일과 고정 이웃 및 도로 장식 선택 결과
            public byte Tile { get; }
            public int AtlasIndex { get; }
            public int Connections { get; }
            public int SurfaceConnections { get; }
            public int Lane { get; }
            public int WorldX { get; }
            public int WorldY { get; }

            // 지도에서 조회한 표시 입력을 픽셀 합성으로 전달
            internal StreetTile(byte tile, int atlasIndex, int connections, int surfaceConnections, int lane, int x, int y)
            {
                // 지도 좌표를 원본 픽셀 격자로 변환해 대시 위상 고정
                Tile = tile;
                AtlasIndex = atlasIndex;
                Connections = connections;
                SurfaceConnections = surfaceConnections;
                Lane = lane;
                WorldX = x * PixelsPerTile;
                WorldY = y * PixelsPerTile;
            }
        }

        // 육해 판정의 상하좌우 및 대각선 고정 이웃
        private static readonly Vector2Int[] Neighbors =
        {
            Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left,
            new Vector2Int(1, 1), new Vector2Int(1, -1),
            new Vector2Int(-1, -1), new Vector2Int(-1, 1)
        };


        // 사용 전 필수 아틀라스 규격 검사와 CPU 읽기 캐시 준비
        public void Prepare()
        {
            // 누락되거나 잘못 임포트된 지형 자산의 명시적 오류 전달
            if (terrainAtlas == null || terrainAtlas.width != AtlasColumns * PixelsPerTile ||
                terrainAtlas.height != AtlasRows * PixelsPerTile || !terrainAtlas.isReadable ||
                terrainAtlas.filterMode != FilterMode.Point)
            {
                throw new InvalidOperationException("지형 아틀라스는 읽기 가능한 64×48 Point 텍스처가 필요합니다");
            }

            atlasPixels = terrainAtlas.GetPixels32();

            // 도로 아틀라스도 지형과 같은 원본 격자와 Point 설정 검사
            if (roadAtlas == null || roadAtlas.width != AtlasColumns * PixelsPerTile ||
                roadAtlas.height != RoadAtlasRows * PixelsPerTile || !roadAtlas.isReadable ||
                roadAtlas.filterMode != FilterMode.Point)
            {
                throw new InvalidOperationException("도로 아틀라스는 읽기 가능한 64×64 Point 텍스처가 필요합니다");
            }

            roadPixels = roadAtlas.GetPixels32();

            // 공용 건물 원본과 렌더링 머티리얼의 명시적 연결 검사
            if (buildingAtlas == null || buildingAtlas.width != AtlasColumns * PixelsPerTile ||
                buildingAtlas.height != BuildingAtlasRows * PixelsPerTile || !buildingAtlas.isReadable ||
                buildingAtlas.filterMode != FilterMode.Point || buildingMaterial == null ||
                buildingMaterial.shader == null || buildingMaterial.mainTexture != buildingAtlas)
            {
                throw new InvalidOperationException("건물 아틀라스 64×304 Point 설정과 공용 머티리얼 연결이 필요합니다");
            }

            buildingPixels = buildingAtlas.GetPixels32();
        }


        // 생성 난수를 소비하지 않는 시드와 타일 좌표 기반 변형 선택
        public int GetTileIndex(CityMap map, int x, int y)
        {
            // 최종 타일의 자연 지형에만 아트 변형 적용
            uint hash = unchecked((uint)map.Seed ^ (uint)x * HorizontalHashMultiplier ^ (uint)y * VerticalHashMultiplier);
            hash ^= hash >> 16;
            hash = unchecked(hash * HashAvalancheMultiplier);
            hash ^= hash >> 15;
            byte tile = map.Tiles[y * map.Size + x];

            if (tile == CityGenerator.Grass || tile == CityGenerator.Building)
            {
                return (int)(hash & 3u);
            }

            if (tile == CityGenerator.Sand)
            {
                return SandStart + (int)(hash & 1u);
            }

            return OceanStart + (int)(hash & 1u);
        }


        // 원본 지형의 고정 이웃에서 해안 연결 비트 수집
        public int GetCoastMask(CityMap map, int x, int y)
        {
            // 도로와 건물 대신 Terrain 배열의 육해 경계 사용
            byte terrain = map.Terrain[y * map.Size + x];

            if (terrain != CityGenerator.Grass && terrain != CityGenerator.Sand)
            {
                return 0;
            }

            int mask = 0;

            for (int direction = 0; direction < Neighbors.Length; direction++)
            {
                int nx = x + Neighbors[direction].x;
                int ny = y + Neighbors[direction].y;

                if (nx < 0 || ny < 0 || nx >= map.Size || ny >= map.Size)
                {
                    continue;
                }

                byte neighbor = map.Terrain[ny * map.Size + nx];

                if (neighbor == CityGenerator.Ocean || neighbor == CityGenerator.Shallows || neighbor == CityGenerator.River)
                {
                    mask |= 1 << direction;
                }
            }

            return mask;
        }


        // 지형 원본과 지역 색조 및 가장 가까운 해안 경계 합성
        public Color32 Sample(int tileIndex, CityRegion region, bool isShallow, int coastMask, int x, int y)
        {
            // 모든 변형이 같은 가장자리 팔레트를 공유하는 원본 픽셀 조회
            Color32 color = ReadPixel(tileIndex, x, y);

            if (coastMask != 0 && tileIndex < OceanStart)
            {
                int nearest = PixelsPerTile;
                int edge = -1;

                for (int direction = 0; direction < CardinalDirectionCount; direction++)
                {
                    int distance = direction == 0 ? PixelsPerTile - 1 - y
                        : direction == 1 ? PixelsPerTile - 1 - x : direction == 2 ? y : x;

                    if ((coastMask & (1 << direction)) != 0 && distance < nearest)
                    {
                        nearest = distance;
                        edge = direction;
                    }
                }

                // 두 해안의 중첩도 가까운 경계 하나로 선택해 모서리 교차선 방지
                if (edge >= 0 && nearest < CoastDepth)
                {
                    Color32 coast = ReadPixel(CoastStart + edge, x, y);

                    if (coast.b > coast.r || (coast.r >= 230 && coast.g >= 210))
                    {
                        return coast;
                    }
                }

                // 대각선에만 물이 있는 오목한 모서리의 한 픽셀 틈새 연결
                for (int corner = 0; corner < CardinalDirectionCount; corner++)
                {
                    int dx = corner < 2 ? PixelsPerTile - 1 - x : x;
                    int dy = corner == 0 || corner == 3 ? PixelsPerTile - 1 - y : y;

                    if ((coastMask & (1 << (CardinalDirectionCount + corner))) != 0 && dx + dy < CornerDepth)
                    {
                        return ReadPixel(CoastStart, PixelsPerTile / 2, PixelsPerTile - 1);
                    }
                }
            }

            // 원본 지역 구분을 유지하는 지형 전용 정수 색조 변환
            if (tileIndex < SandStart)
            {
                if (region == CityRegion.Natural)
                {
                    return Tint(color, 82, 89, 85);
                }

                if (region == CityRegion.Downtown)
                {
                    return Tint(color, 108, 98, 105);
                }

                if (region == CityRegion.Industrial)
                {
                    return Tint(color, 112, 98, 88);
                }

                if (region == CityRegion.Riverside)
                {
                    return Tint(color, 102, 106, 108);
                }
            }

            if (isShallow)
            {
                return Tint(color, 133, 132, 117);
            }

            return color;
        }


        // 최종 타일의 도로와 보행 표면 여부 조회
        public static bool IsStreet(byte tile)
        {
            // 시뮬레이션의 타일 종류를 변경하지 않는 표시 전용 분류
            return tile == CityGenerator.Road || tile == CityGenerator.Pavement ||
                tile == CityGenerator.Path || tile == CityGenerator.Bridge;
        }


        // 고정 이웃과 짧은 폭 검사에서 도로 합성 입력 선택
        public StreetTile GetStreetTile(CityMap map, int x, int y)
        {
            // 원본 최종 타일을 기준으로 차도와 보행 표면의 연결 마스크 분리
            byte tile = map.Tiles[y * map.Size + x];
            int connections = 0;
            int surfaceConnections = 0;

            for (int direction = 0; direction < Neighbors.Length; direction++)
            {
                byte neighbor = TileAt(map, x + Neighbors[direction].x, y + Neighbors[direction].y);

                if (IsCarriageway(neighbor))
                {
                    connections |= 1 << direction;
                }

                if (IsStreet(neighbor) || neighbor == CityGenerator.Building)
                {
                    surfaceConnections |= 1 << direction;
                }
            }

            uint hash = unchecked((uint)map.Seed ^ (uint)x * HorizontalHashMultiplier ^ (uint)y * VerticalHashMultiplier);
            int atlasIndex = (int)((hash ^ (hash >> 16)) & 1u);
            int lane = tile == CityGenerator.Road ? SelectLane(map, x, y) : 0;

            if (tile == CityGenerator.Pavement)
            {
                atlasIndex = PavementTile;
            }
            else if (tile == CityGenerator.Path)
            {
                atlasIndex = EntranceTile;
            }
            else if (tile == CityGenerator.Bridge)
            {
                // 기존 교량 Bounds에서 진행 축을 가져와 짧은 교량 끝의 오판 방지
                bool isHorizontal = true;

                foreach (var bridge in map.Bridges)
                {
                    if (bridge.Contains(new Vector2Int(x, y)))
                    {
                        isHorizontal = bridge.width >= bridge.height;
                        break;
                    }
                }

                atlasIndex = isHorizontal ? HorizontalBridge : VerticalBridge;
            }

            return new StreetTile(tile, atlasIndex, connections, surfaceConnections, lane, x, y);
        }


        // 연결 마스크로 도로와 보도 및 교량의 16픽셀 원본 합성
        public Color32 SampleStreet(in StreetTile street, int x, int y)
        {
            // 실제 타일 전체를 불투명 표면으로 채워 대각선과 구역 경계의 빈틈 방지
            Color32 color = ReadRoadPixel(street.AtlasIndex, x, y);

            if (street.Tile == CityGenerator.Bridge)
            {
                bool isHorizontal = street.AtlasIndex == HorizontalBridge;

                for (int direction = isHorizontal ? 0 : 1; direction < CardinalDirectionCount; direction += 2)
                {
                    int distance = EdgeDistance(direction, x, y);

                    if ((street.SurfaceConnections & (1 << direction)) == 0 && distance < BridgeRailDepth)
                    {
                        int along = isHorizontal ? street.WorldX + x : street.WorldY + y;
                        return distance == 0 || along % BridgePostSpacing == 0 ? CurbLight : CurbShadow;
                    }
                }

                return color;
            }

            if (street.Tile == CityGenerator.Pavement || street.Tile == CityGenerator.Path)
            {
                for (int direction = 0; direction < CardinalDirectionCount; direction++)
                {
                    int distance = EdgeDistance(direction, x, y);
                    bool hasRoad = (street.Connections & (1 << direction)) != 0;
                    bool hasSurface = (street.SurfaceConnections & (1 << direction)) != 0;

                    if (distance < CurbDepth && hasRoad)
                    {
                        return distance == 0 && (direction == 0 || direction == 3) ? CurbLight : CurbShadow;
                    }

                    if (distance == 0 && !hasSurface)
                    {
                        return direction == 0 || direction == 3 ? CurbLight : CurbShadow;
                    }
                }

                return color;
            }

            // 노출된 외곽 한 픽셀만 조각으로 합성해 기존 차도 폭 유지
            for (int direction = 0; direction < CardinalDirectionCount; direction++)
            {
                if ((street.Connections & (1 << direction)) == 0 && EdgeDistance(direction, x, y) == 0)
                {
                    return ReadRoadPixel(RoadEdgeStart + direction, x, y);
                }
            }

            for (int corner = 0; corner < CardinalDirectionCount; corner++)
            {
                int dx = corner < 2 ? PixelsPerTile - 1 - x : x;
                int dy = corner == 0 || corner == 3 ? PixelsPerTile - 1 - y : y;

                if ((street.Connections & (1 << (CardinalDirectionCount + corner))) == 0 && dx + dy < CornerDepth)
                {
                    return ReadRoadPixel(RoadCornerStart + corner, x, y);
                }
            }

            // 충분한 폭과 일정한 진행 축을 확보한 구간에만 절제된 중앙 점선 표시
            bool isLanePixel = street.Lane == HorizontalLane ? y == 0 : street.Lane == VerticalLane ? x == 0
                : street.Lane == RisingLane ? x == y : street.Lane == FallingLane && x + y == PixelsPerTile - 1;
            int alongLane = street.Lane == VerticalLane ? street.WorldY + y : street.WorldX + x;

            if (isLanePixel && alongLane % DashPeriod < DashLength)
            {
                int tile = street.Lane == VerticalLane ? VerticalMarking : HorizontalMarking;
                return ReadRoadPixel(tile, PixelsPerTile / 2, PixelsPerTile / 2);
            }

            return color;
        }


        // 짧은 고정 반경의 폭과 진행 방향에서 차선 표시 위치 결정
        private static int SelectLane(CityMap map, int x, int y)
        {
            // 교차로처럼 양 축이 모두 넓은 영역과 좁은 진입로의 차선 제외
            int left = RoadRun(map, x, y, -1, 0);
            int right = RoadRun(map, x, y, 1, 0);
            int down = RoadRun(map, x, y, 0, -1);
            int up = RoadRun(map, x, y, 0, 1);
            int width = left + right + 1;
            int height = down + up + 1;

            if (height >= MinimumLaneWidth && height <= MaximumLaneWidth && width >= height + DirectionDifference &&
                left == RoadProbeRadius && right == RoadProbeRadius)
            {
                return down == height / 2 ? HorizontalLane : 0;
            }

            if (width >= MinimumLaneWidth && width <= MaximumLaneWidth && height >= width + DirectionDifference &&
                up == RoadProbeRadius && down == RoadProbeRadius)
            {
                return left == width / 2 ? VerticalLane : 0;
            }

            int northEast = RoadRun(map, x, y, 1, 1);
            int southWest = RoadRun(map, x, y, -1, -1);
            int northWest = RoadRun(map, x, y, -1, 1);
            int southEast = RoadRun(map, x, y, 1, -1);
            int rising = northEast + southWest + 1;
            int falling = northWest + southEast + 1;

            if (falling >= MinimumDiagonalWidth && falling <= MaximumDiagonalWidth &&
                northEast == RoadProbeRadius && southWest == RoadProbeRadius)
            {
                return southEast == falling / 2 ? RisingLane : 0;
            }

            if (rising >= MinimumDiagonalWidth && rising <= MaximumDiagonalWidth &&
                northWest == RoadProbeRadius && southEast == RoadProbeRadius)
            {
                return southWest == rising / 2 ? FallingLane : 0;
            }

            return 0;
        }


        // 도로 폭과 방향 판정에 필요한 최대 다섯 이웃만 조회
        private static int RoadRun(CityMap map, int x, int y, int dx, int dy)
        {
            // 구역 생성 때만 수행하는 고정 상한 탐색
            int length = 0;

            for (int distance = 1; distance <= RoadProbeRadius; distance++)
            {
                if (!IsCarriageway(TileAt(map, x + dx * distance, y + dy * distance)))
                {
                    break;
                }

                length++;
            }

            return length;
        }


        // 차도와 교량의 연결 가능 여부 조회
        private static bool IsCarriageway(byte tile)
        {
            // 보행 진입로를 차도 폭에 포함하지 않는 표시 분류
            return tile == CityGenerator.Road || tile == CityGenerator.Bridge;
        }


        // 경계 밖을 바다로 취급하는 읽기 전용 최종 타일 조회
        private static byte TileAt(CityMap map, int x, int y)
        {
            // 지도 외부의 도로 연결과 배열 범위 초과 방지
            return x < 0 || y < 0 || x >= map.Size || y >= map.Size
                ? CityGenerator.Ocean : map.Tiles[y * map.Size + x];
        }


        // 픽셀에서 해당 방향의 타일 가장자리까지 거리 계산
        private static int EdgeDistance(int direction, int x, int y)
        {
            // 상우하좌 연결 비트와 같은 순서로 거리 반환
            return direction == 0 ? PixelsPerTile - 1 - y : direction == 1 ? PixelsPerTile - 1 - x : direction == 2 ? y : x;
        }


        // 도로 아틀라스의 정수 픽셀 조회
        private Color32 ReadRoadPixel(int tileIndex, int x, int y)
        {
            // Point 원본 격자의 표면과 연결 조각을 보간 없이 반환
            int column = tileIndex % AtlasColumns;
            int row = RoadAtlasRows - 1 - tileIndex / AtlasColumns;
            return roadPixels[(row * PixelsPerTile + y) * AtlasColumns * PixelsPerTile + column * PixelsPerTile + x];
        }


        // 시드와 실제 종류 및 위치에서 건물 형태와 색상 변형 선택
        public BuildingStyle GetBuildingStyle(int seed, CityBuilding building)
        {
            // 다른 도시 난수 흐름을 소비하지 않는 좌표와 종류 해시 구성
            uint hash = unchecked((uint)seed ^ (uint)building.Bounds.x * HorizontalHashMultiplier ^
                (uint)building.Bounds.y * VerticalHashMultiplier ^ (uint)building.Type * HashAvalancheMultiplier);
            hash ^= hash >> 16;
            hash = unchecked(hash * HashAvalancheMultiplier);
            int variation = (int)(hash >> 16);
            BuildingType type = building.Type;

            // 실제 건물 종류를 정면 시점 형태로 대응하고 종류 미지정 건물은 생성 분류 사용
            BuildingForm form = type switch
            {
                BuildingType.DetachedHouse or BuildingType.CountryHouse or BuildingType.RiversideHouse or
                    BuildingType.Cabin => BuildingForm.House,
                BuildingType.Villa or BuildingType.StaffDormitory => BuildingForm.Villa,
                BuildingType.Apartment => BuildingForm.Apartment,
                BuildingType.OfficeBuilding => BuildingForm.Office,
                BuildingType.Hotel => BuildingForm.Hotel,
                BuildingType.Hypermarket => BuildingForm.Mart,
                BuildingType.DepartmentStore => BuildingForm.DepartmentStore,
                BuildingType.Hospital => BuildingForm.Hospital,
                BuildingType.PoliceStation => BuildingForm.Police,
                BuildingType.VillageHall or BuildingType.HealthCenter or BuildingType.CampsiteOffice => BuildingForm.Civic,
                BuildingType.PublicRestroom => BuildingForm.Restroom,
                BuildingType.Guardhouse or BuildingType.CoastalOutpost => BuildingForm.Outpost,
                BuildingType.Factory => BuildingForm.Factory,
                BuildingType.FarmToolStorage or BuildingType.SmallWarehouse or BuildingType.LogisticsWarehouse or
                    BuildingType.RepairShop => BuildingForm.Warehouse,
                BuildingType.Unspecified => building.Kind == 3 ? BuildingForm.Office
                    : building.Kind == 2 ? BuildingForm.Factory : BuildingForm.House,
                _ => BuildingForm.Shop
            };

            // 업종과 시설을 한눈에 구분하는 간판 아이콘과 대표색 선택
            (BuildingPart sign, Color32 accent) = type switch
            {
                BuildingType.ConvenienceStore => (BuildingPart.ConvenienceIcon, new Color32(40, 160, 150, 255)),
                BuildingType.NeighborhoodMarket or BuildingType.SmallSupermarket =>
                    (BuildingPart.MartIcon, new Color32(226, 120, 40, 255)),
                BuildingType.Hypermarket => (BuildingPart.MartIcon, new Color32(206, 60, 46, 255)),
                BuildingType.Pharmacy => (BuildingPart.PharmacyIcon, new Color32(60, 150, 90, 255)),
                BuildingType.Restaurant or BuildingType.RiversideRestaurant =>
                    (BuildingPart.FoodIcon, new Color32(200, 60, 50, 255)),
                BuildingType.Cafe => (BuildingPart.CafeIcon, new Color32(130, 88, 60, 255)),
                BuildingType.HardwareStore or BuildingType.ToolStore or BuildingType.RepairShop =>
                    (BuildingPart.ToolIcon, new Color32(226, 176, 50, 255)),
                BuildingType.FishingSupplyStore => (BuildingPart.FishIcon, new Color32(50, 110, 180, 255)),
                BuildingType.DepartmentStore => (BuildingPart.BagIcon, new Color32(40, 110, 80, 255)),
                BuildingType.Hotel => (BuildingPart.HotelIcon, new Color32(176, 58, 136, 255)),
                BuildingType.Hospital => (BuildingPart.HospitalIcon, new Color32(206, 52, 48, 255)),
                BuildingType.PoliceStation => (BuildingPart.PoliceIcon, new Color32(40, 64, 140, 255)),
                BuildingType.VillageHall or BuildingType.CampsiteOffice => (BuildingPart.CivicIcon, new Color32(60, 100, 150, 255)),
                BuildingType.HealthCenter => (BuildingPart.PharmacyIcon, new Color32(60, 150, 90, 255)),
                BuildingType.PublicRestroom => (BuildingPart.RestroomIcon, new Color32(60, 120, 180, 255)),
                BuildingType.Guardhouse => (BuildingPart.GuardIcon, new Color32(46, 70, 120, 255)),
                BuildingType.CoastalOutpost => (BuildingPart.GuardIcon, new Color32(50, 96, 70, 255)),
                BuildingType.Apartment => (BuildingPart.None, ApartmentAccents[variation % ApartmentAccents.Length]),
                _ => form == BuildingForm.Shop || form == BuildingForm.Office
                    ? (BuildingPart.OfficeIcon, new Color32(50, 70, 120, 255)) : (BuildingPart.None, White)
            };
            bool hasAwning = type == BuildingType.Restaurant || type == BuildingType.RiversideRestaurant ||
                type == BuildingType.Cafe || type == BuildingType.NeighborhoodMarket || type == BuildingType.SmallSupermarket;
            // 공장 굴뚝만 지붕 위로 올라가므로 선택과 앞뒤 판정의 표시 높이에 반영
            int height = form == BuildingForm.Factory ? FactoryStackRise : 0;
            return new BuildingStyle(form, height, variation, sign, accent, hasAwning);
        }


        // 건물과 유닛이 공유하는 지면 좌표의 깊이 순서 계산
        internal static int GetDepthOrder(float groundY)
        {
            // 1024 지도에서도 SpriteRenderer 순서 범위 안에 유형별 네 단계 여유 확보
            int depth = Mathf.Clamp(Mathf.RoundToInt(groundY * DepthStepsPerTile), 0, MaximumMapSize * DepthStepsPerTile);
            return FirstBuildingOrder + (MaximumMapSize * DepthStepsPerTile - depth) * DepthStride;
        }


        // 건물 조각 안의 잘라 쓰는 영역을 공용 아틀라스 좌표로 변환
        internal RectInt GetBuildingRegion(BuildingPart part, RectInt section)
        {
            // 조각을 확대하지 않고 실제 원본 픽셀 크기로 반복할 영역 전달
            int index = (int)part;
            return new RectInt(index % AtlasColumns * PixelsPerTile + section.x,
                (BuildingAtlasRows - 1 - index / AtlasColumns) * PixelsPerTile + section.y, section.width, section.height);
        }


        // 축소 지도 합성용 공용 건물 원본 픽셀 조회
        internal Color32 GetBuildingPixel(int x, int y)
        {
            // 읽기 캐시에서 보간 없이 원본 색상과 투명도 전달
            return buildingPixels[y * AtlasColumns * PixelsPerTile + x];
        }


        // 상단부터 정렬한 아틀라스 번호를 Unity 하단 원점으로 변환
        private Color32 ReadPixel(int tileIndex, int x, int y)
        {
            // 16픽셀 원본 격자의 색상을 보간 없이 반환
            int column = tileIndex % AtlasColumns;
            int row = AtlasRows - 1 - tileIndex / AtlasColumns;
            return atlasPixels[(row * PixelsPerTile + y) * AtlasColumns * PixelsPerTile + column * PixelsPerTile + x];
        }


        // 소재 명암을 보존하는 정수 백분율 색조 계산
        private static Color32 Tint(Color32 color, int red, int green, int blue)
        {
            // 채널 상한을 적용해 지역별 질감의 오버플로 방지
            return new Color32((byte)Mathf.Min(255, color.r * red / 100),
                (byte)Mathf.Min(255, color.g * green / 100), (byte)Mathf.Min(255, color.b * blue / 100), color.a);
        }
    }
}
