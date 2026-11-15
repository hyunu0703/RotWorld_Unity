using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace RotWorld
{
    /// <summary>
    /// 공용 건물 조각을 크기별 메쉬와 축소 지도 픽셀로 조합
    /// </summary>
    internal sealed class CityBuildingArt
    {
        // 원본 조각과 테두리 및 그림자의 픽셀 규격
        private const int TilePixels = CityArtCatalog.PixelsPerTile;
        private const int Border = 3;
        private const int RoofRidgeHeight = 2;
        private const int SmallWallHeight = 12;
        private const int DoorWidth = 8;
        private const int SideDoorWidth = 6;
        private const int ShadowSoftShade = 232;
        private const int ShadowMainShade = 182;
        private static readonly Color32 White = new Color32(255, 255, 255, 255);
        private static readonly Color32 RoofShade = new Color32(205, 214, 219, 255);
        private static readonly Color32 SideShade = new Color32(163, 179, 187, 255);
        private static readonly RectInt FullTile = new RectInt(0, 0, TilePixels, TilePixels);

        // 정면 시점 형태의 층과 마당 및 담장 규격
        private const int FloorHeight = 16;
        private const int MinimumYard = 12;
        private const int FenceHeight = 8;
        private const int FenceSideWidth = 4;
        private const int GateWidth = 16;
        private const int GatePostHeight = 12;
        private const int HouseBaseHeight = 4;
        private const int HouseFloors = 2;
        private const int VillaFloors = 3;
        private const int ShopGroundHeight = 30;
        private const int ShopParapetHeight = 3;
        private const int ShopSignOffset = 20;
        private const int SignHeight = 10;
        private const int GlyphSpacing = 13;
        private const int StorefrontHeight = 14;
        private const int DoorPatchHeight = 14;
        private const int RoofOverhang = 2;
        private const int EaveRows = 3;
        private const int HipShadeWidth = 3;
        private const int SideFaceWidth = 3;
        private const int MetalRoofSlopeRows = 4;
        private const int SawToothHeight = 14;
        private const int ShedSpacing = 80;
        private const int ShutterWidth = 24;
        private const int ShutterHeight = 27;
        private const int BandWindowHeight = 6;
        private const int BandWindowSpacing = 24;
        private const int OfficeWidth = 56;
        private const int OfficeOverlap = 18;
        private const int OfficeMinimumBodyWidth = 160;
        private const int OfficeMinimumYard = 40;
        private const int ShedWallHeight = 48;
        private const int ShedRoofHeight = 16;
        private const int ShedMinimumYard = 150;
        private const int ShedMinimumBodyWidth = 140;
        private const int ContainerWidth = 32;
        private const int ContainerHeight = 10;
        private const int ContainerSpacing = 36;
        private const int ContainerMinimumYard = 44;
        private const int MinimumIndustrialWall = 48;
        private const int MaximumIndustrialWall = 96;
        private const int IndustrialWallPercent = 35;
        private const int BodyShadowOffset = 3;
        private const int BodyShadowShade = 178;
        private const int ParkingLineSpacing = 18;
        private const int OfficeWall = FloorHeight * 2 + 2;
        private static readonly Color32 Outline = new Color32(38, 36, 44, 255);
        private static readonly Color32 EaveShadow = new Color32(60, 56, 60, 255);
        private static readonly Color32 RoofRim = new Color32(226, 224, 216, 255);
        private static readonly Color32 YardCement = new Color32(226, 220, 206, 255);
        private static readonly Color32 IndustrialConcrete = new Color32(200, 202, 204, 255);
        private static readonly Color32 IndustrialFence = new Color32(200, 204, 208, 255);
        private static readonly Color32 ParkingLine = new Color32(226, 226, 216, 255);
        private static readonly Color32 SafetyYellow = new Color32(230, 190, 60, 255);
        private static readonly Color32[] HouseWallTints =
        {
            new Color32(255, 246, 226, 255), new Color32(246, 246, 240, 255),
            new Color32(252, 228, 216, 255), new Color32(226, 238, 226, 255)
        };
        private static readonly Color32[] HouseRoofTints =
        {
            new Color32(214, 104, 82, 255), new Color32(96, 136, 190, 255),
            new Color32(112, 160, 112, 255), new Color32(128, 128, 140, 255)
        };
        private static readonly Color32[] ShopWallTints =
        {
            new Color32(232, 232, 228, 255), new Color32(240, 226, 200, 255), new Color32(214, 226, 236, 255)
        };
        private static readonly Color32[] FlatRoofTints =
        {
            new Color32(150, 196, 146, 255), new Color32(206, 204, 196, 255)
        };
        private static readonly Color32[] MetalWallTints =
        {
            new Color32(238, 230, 204, 255), new Color32(196, 216, 230, 255), new Color32(194, 210, 192, 255)
        };
        private static readonly Color32[] MetalRoofTints =
        {
            new Color32(150, 160, 170, 255), new Color32(110, 146, 184, 255), new Color32(176, 96, 76, 255)
        };
        private static readonly Color32[] ContainerTints =
        {
            new Color32(216, 122, 62, 255), new Color32(72, 122, 182, 255),
            new Color32(82, 142, 102, 255), new Color32(178, 66, 58, 255)
        };

        // 조합할 건물과 카탈로그 및 변경하지 않는 외형 선택
        private readonly CityArtCatalog art;
        private readonly CityBuilding building;
        private readonly CityArtCatalog.BuildingStyle style;
        private readonly FrontLayout layout;

        // 메쉬 생성에서만 소유하는 작은 기하 버퍼
        private readonly List<Vector3> vertices;
        private readonly List<Vector2> coordinates;
        private readonly List<Vector4> regions;
        private readonly List<Color32> colors;
        private readonly List<int> triangles;

        // 축소 지도 합성에서만 사용하는 외부 소유 픽셀 대상
        private NativeArray<Color32> pixels;
        private readonly int stride;
        private readonly float scale;
        private readonly Vector2Int origin;


        // 메쉬 조합용 기하 버퍼 준비
        private CityBuildingArt(CityArtCatalog art, CityBuilding building, int seed)
        {
            // 동일한 조합 규칙에서 메쉬와 축소 지도 출력 경로 구분
            this.art = art;
            this.building = building;
            style = art.GetBuildingStyle(seed, building);
            layout = new FrontLayout(building, style);
            vertices = new List<Vector3>();
            coordinates = new List<Vector2>();
            regions = new List<Vector4>();
            colors = new List<Color32>();
            triangles = new List<int>();
        }


        // 축소 지도 조합용 외부 소유 버퍼 연결
        private CityBuildingArt(CityArtCatalog art, CityBuilding building, int seed,
            NativeArray<Color32> pixels, int stride, float scale)
        {
            // 건물 원점만 원본 픽셀로 환산하고 그래픽 선택은 메쉬와 동일하게 유지
            this.art = art;
            this.building = building;
            style = art.GetBuildingStyle(seed, building);
            layout = new FrontLayout(building, style);
            this.pixels = pixels;
            this.stride = stride;
            this.scale = scale;
            origin = new Vector2Int(building.Bounds.x * TilePixels, building.Bounds.y * TilePixels);
        }


        // 건물 크기에 맞는 반복 조각 메쉬 생성
        internal static Mesh CreateMesh(CityArtCatalog art, CityBuilding building, int seed)
        {
            // 조각 반복은 공용 텍스처의 원본 크기를 유지하고 기하 사각형만 확장
            var composer = new CityBuildingArt(art, building, seed);
            composer.Compose();
            var mesh = new Mesh();
            mesh.name = $"City building {building.Type} {building.Bounds.x},{building.Bounds.y}";
            mesh.SetVertices(composer.vertices);
            mesh.SetUVs(0, composer.coordinates);
            mesh.SetUVs(1, composer.regions);
            mesh.SetColors(composer.colors);
            mesh.SetTriangles(composer.triangles, 0);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }


        // 축소 지도에 그림자와 깊이 순서의 건물 본체 조합
        internal static void PaintOverview(CityMap map, CityArtCatalog art, NativeArray<Color32> pixels, int stride, float pixelsPerUnit)
        {
            // 전체 지도용으로 한 번만 정렬하고 생성 데이터의 목록 순서 보존
            var ordered = new List<CityBuilding>(map.Buildings);
            ordered.Sort((a, b) => b.Bounds.yMin.CompareTo(a.Bounds.yMin));
            float scale = pixelsPerUnit / TilePixels;

            foreach (var building in ordered)
            {
                PaintGround(art, building, map.Seed, pixels, stride, Vector2Int.zero, scale);
            }

            foreach (var building in ordered)
            {
                var composer = new CityBuildingArt(art, building, map.Seed, pixels, stride, scale);
                composer.Compose();
            }
        }


        // 고정 지형 버퍼에 우하단 그림자와 실제 출입구의 문턱 표시
        internal static void PaintGround(CityArtCatalog art, CityBuilding building, int seed,
            NativeArray<Color32> pixels, int stride, Vector2Int chunkOrigin, float scale = 1f)
        {
            // 논리 Bounds와 Door는 그대로 사용하고 표시 좌표만 변환
            CityArtCatalog.BuildingStyle style = art.GetBuildingStyle(seed, building);
            int x = (building.Bounds.x - chunkOrigin.x) * TilePixels;
            int y = (building.Bounds.y - chunkOrigin.y) * TilePixels;

            // 정면 형태는 마당과 본체 그림자, 기존 형태는 필지 전체 그림자 합성
            if (style.Form != CityArtCatalog.BuildingForm.Legacy)
            {
                PaintFrontGround(art, new FrontLayout(building, style), style, pixels, stride,
                    new Vector2Int(x, y), chunkOrigin * TilePixels, scale);
            }
            else
            {
                int width = building.Bounds.width * TilePixels;
                int height = building.Bounds.height * TilePixels;
                int dx = Border + style.Height / 4;
                int dy = Border + style.Height / 3;
                Shade(pixels, stride, new RectInt(x + dx - 1, y - dy - 1, width + 2, height + 2), scale, ShadowSoftShade);
                Shade(pixels, stride, new RectInt(x + dx, y - dy, width, height), scale, ShadowMainShade);
            }

            int doorX = (building.Door.x - chunkOrigin.x) * TilePixels + TilePixels / 4;
            int doorY = (building.Door.y - chunkOrigin.y) * TilePixels + TilePixels / 4;
            var step = new RectInt(doorX, doorY, TilePixels / 2, TilePixels / 2);
            int minX = Mathf.Max(0, Mathf.CeilToInt(step.x * scale - .5f));
            int minY = Mathf.Max(0, Mathf.CeilToInt(step.y * scale - .5f));
            int maxX = Mathf.Min(stride, Mathf.CeilToInt(step.xMax * scale - .5f));
            int maxY = Mathf.Min(stride, Mathf.CeilToInt(step.yMax * scale - .5f));

            for (int py = minY; py < maxY; py++)
            {
                for (int px = minX; px < maxX; px++)
                {
                    pixels[py * stride + px] = new Color32(193, 180, 148, 255);
                }
            }
        }


        // 높이 확장을 포함하는 건물의 표시 영역 조회
        internal static Rect GetVisualBounds(CityBuilding building, CityArtCatalog.BuildingStyle style)
        {
            // 지면 충돌 범위와 별개인 지붕 표시 영역 반환
            RectInt bounds = building.Bounds;
            return new Rect(bounds.x, bounds.y, bounds.width, bounds.height + style.Height / (float)TilePixels);
        }


        // 벽과 지붕 및 테두리와 종류별 설비의 공통 조합 규칙 실행
        private void Compose()
        {
            // 주택과 상가 및 공장은 벽 3 대 지붕 1 정면 시점 규칙으로 분기
            if (style.Form != CityArtCatalog.BuildingForm.Legacy)
            {
                ComposeFront();
                return;
            }

            // 지면에서 위로 올린 지붕과 제한된 높이의 정면 벽 구성
            int width = building.Bounds.width * TilePixels;
            int depth = building.Bounds.height * TilePixels;
            int height = style.Height;
            RectInt wallSource = height <= SmallWallHeight ? new RectInt(0, 4, TilePixels, 8) : FullTile;
            Patch(new RectInt(0, 0, width, height), style.Wall, wallSource, White);
            Patch(new RectInt(width - Border, 0, Border, height), style.Wall, wallSource, SideShade);

            if (style.Category == CityArtCatalog.BuildingCategory.Residential)
            {
                Patch(new RectInt(DoorWidth, 0, TilePixels, Mathf.Min(height, DoorWidth)), CityArtCatalog.BuildingPart.Windows,
                    new RectInt(0, 4, TilePixels, DoorWidth), White);
            }

            var roof = new RectInt(0, height, width, depth);

            if (style.IsPitched)
            {
                int half = depth / 2;
                Patch(new RectInt(0, height, width, half), style.Roof, FullTile, RoofShade);
                Patch(new RectInt(0, height + half, width, depth - half), style.Roof, FullTile, style.RoofTint);
                Patch(new RectInt(Border, height + half - 1, width - Border * 2, RoofRidgeHeight),
                    CityArtCatalog.BuildingPart.Ridge, new RectInt(0, 7, TilePixels, RoofRidgeHeight), style.RoofTint);
            }
            else
            {
                Patch(roof, style.Roof, FullTile, style.RoofTint);
            }

            Frame(roof);

            // 공통 옥상 설비를 크기와 종류에 맞는 고정 위치에 배치
            Patch(new RectInt(width - TilePixels - Border * 2, height + depth - TilePixels - Border * 2,
                TilePixels, TilePixels), style.Detail, FullTile, White);

            if (!style.IsPitched)
            {
                Patch(new RectInt(TilePixels, height + TilePixels, TilePixels, TilePixels),
                    CityArtCatalog.BuildingPart.Hatch, FullTile, White);

                if (style.Category == CityArtCatalog.BuildingCategory.HighRise)
                {
                    Patch(new RectInt(width / 2, height + depth / 2, TilePixels, TilePixels),
                        CityArtCatalog.BuildingPart.WaterTank, FullTile, White);
                }
            }

            Door(width, depth, height);

            if (style.Sign != CityArtCatalog.BuildingPart.None)
            {
                Patch(new RectInt(width / 2 - TilePixels / 2, height + Border, TilePixels, TilePixels), style.Sign, FullTile, White);
            }
        }


        // 아홉 조각의 지붕 테두리를 늘리지 않고 반복 조합
        private void Frame(RectInt roof)
        {
            // 네 모서리는 원본 크기 유지와 네 가장자리의 중간 부분 반복
            for (int row = 0; row < 3; row++)
            {
                for (int column = 0; column < 3; column++)
                {
                    if (row == 1 && column == 1)
                    {
                        continue;
                    }

                    int sourceX = column == 0 ? 0 : column == 1 ? Border : TilePixels - Border;
                    int sourceY = row == 0 ? 0 : row == 1 ? Border : TilePixels - Border;
                    int sourceWidth = column == 1 ? TilePixels - Border * 2 : Border;
                    int sourceHeight = row == 1 ? TilePixels - Border * 2 : Border;
                    int x = column == 0 ? roof.x : column == 1 ? roof.x + Border : roof.xMax - Border;
                    int y = row == 0 ? roof.y : row == 1 ? roof.y + Border : roof.yMax - Border;
                    int width = column == 1 ? roof.width - Border * 2 : Border;
                    int height = row == 1 ? roof.height - Border * 2 : Border;
                    Patch(new RectInt(x, y, width, height), CityArtCatalog.BuildingPart.Parapet,
                        new RectInt(sourceX, sourceY, sourceWidth, sourceHeight), style.RoofTint);
                }
            }
        }


        // 실제 Door가 연결된 면에 맞춰 출입문 조각 배치
        private void Door(int width, int depth, int height)
        {
            // 출입구 데이터는 변경하지 않고 입체 외형 위의 투영 위치만 선택
            int x = (building.Door.x - building.Bounds.x) * TilePixels + TilePixels / 2;
            int y = (building.Door.y - building.Bounds.y) * TilePixels + TilePixels / 2;
            int doorWidth = style.Door == CityArtCatalog.BuildingPart.ShutterDoor ? TilePixels : DoorWidth;
            RectInt target;

            if (building.Door.y < building.Bounds.yMin)
            {
                target = new RectInt(Mathf.Clamp(x - doorWidth / 2, Border, width - doorWidth - Border), 0, doorWidth, Mathf.Min(height, TilePixels));
            }
            else if (building.Door.y >= building.Bounds.yMax)
            {
                target = new RectInt(Mathf.Clamp(x - doorWidth / 2, Border, width - doorWidth - Border),
                    depth + height - DoorWidth, doorWidth, DoorWidth);
            }
            else
            {
                int sideX = building.Door.x < building.Bounds.x ? 0 : width - SideDoorWidth;
                target = new RectInt(sideX, Mathf.Clamp(y - DoorWidth / 2 + height / 2, height, depth), SideDoorWidth, SmallWallHeight);
            }

            Patch(target, style.Door, new RectInt((TilePixels - doorWidth) / 2, 0, doorWidth, TilePixels), White);
        }


        // 형태별 담장과 본체 및 부속 시설 조합 순서 결정
        private void ComposeFront()
        {
            // 같은 메쉬 안에서 뒤쪽 담장부터 앞쪽 부속 시설 순서로 덮어쓰기
            switch (style.Form)
            {
                case CityArtCatalog.BuildingForm.House:
                case CityArtCatalog.BuildingForm.Villa:
                    DrawFence(White);
                    ComposeResidence();
                    break;
                case CityArtCatalog.BuildingForm.Shop:
                    ComposeShop();
                    break;
                default:
                    DrawFence(IndustrialFence);
                    ComposeIndustrial();
                    break;
            }
        }


        // 단독주택의 기와지붕과 빌라의 필로티 및 평지붕 조합
        private void ComposeResidence()
        {
            // 외벽 재질과 색상은 변형값으로 선택하고 우측 면을 어둡게 표시
            RectInt body = layout.Body;
            int x = body.x;
            int y = body.y;
            int width = body.width;
            int wall = layout.Wall;
            int variation = style.Variation;
            bool isVilla = style.Form == CityArtCatalog.BuildingForm.Villa;
            bool isBrick = isVilla || (variation >> 2) % 4 == 0;
            Color32 wallTint = HouseWallTints[variation % HouseWallTints.Length];
            CityArtCatalog.BuildingPart wallPart = isBrick ? CityArtCatalog.BuildingPart.BrickWall : CityArtCatalog.BuildingPart.PlasterWall;
            Fill(x - 1, y, width + 2, wall, Outline);
            Patch(x, y, width, wall, wallPart, FullTile, isBrick ? White : wallTint);
            Patch(x + width - SideFaceWidth, y, SideFaceWidth, wall, wallPart, FullTile,
                isBrick ? new Color32(190, 190, 190, 255) : Darken(wallTint, 80));

            if (isVilla)
            {
                // 1층 필로티 주차장과 기둥 및 공용 현관
                Fill(x, y, width, FloorHeight, new Color32(52, 52, 60, 255));
                Fill(x, y + FloorHeight - 2, width, 2, new Color32(200, 198, 190, 255));

                for (int pillar = x; pillar <= x + width - SideFaceWidth; pillar += 22)
                {
                    Fill(pillar, y, 3, FloorHeight, new Color32(204, 202, 194, 255));
                    Fill(pillar + 2, y, 1, FloorHeight - 2, new Color32(160, 158, 150, 255));
                }

                Fill(x + width - 3, y, 3, FloorHeight, new Color32(204, 202, 194, 255));
                Patch(layout.DoorX - TilePixels / 2, y, TilePixels, DoorPatchHeight, CityArtCatalog.BuildingPart.ShopDoor,
                    new RectInt(0, 0, TilePixels, DoorPatchHeight), White);

                // 2층부터 층 구분선과 알루미늄 창 반복
                for (int floor = 1; floor < layout.Floors; floor++)
                {
                    int floorY = y + floor * FloorHeight;
                    Fill(x, floorY, width - SideFaceWidth, 1, new Color32(150, 80, 62, 255));

                    for (int windowX = x + 4; windowX + 14 <= x + width - 5; windowX += 18)
                    {
                        Patch(windowX, floorY + 3, 14, 10, CityArtCatalog.BuildingPart.UpperWindow, new RectInt(1, 0, 14, 10), White);
                    }
                }
            }
            else
            {
                // 기단과 층별 방범창 및 일부 판자로 막은 창
                Fill(x, y, width, HouseBaseHeight, new Color32(126, 124, 120, 255));
                Fill(x, y + HouseBaseHeight - 1, width, 1, new Color32(160, 158, 152, 255));

                for (int floor = 0; floor < layout.Floors; floor++)
                {
                    int floorY = y + HouseBaseHeight + floor * FloorHeight;

                    if (floor > 0)
                    {
                        Fill(x, floorY - 1, width - SideFaceWidth, 1, isBrick ? new Color32(130, 70, 54, 255) : Darken(wallTint, 72));
                    }

                    int index = 0;

                    for (int windowX = x + 5; windowX + 8 <= x + width - 6; windowX += 18)
                    {
                        bool isDoorSlot = floor == 0 && windowX < layout.DoorX + 7 && windowX + 8 > layout.DoorX - 7;

                        if (!isDoorSlot)
                        {
                            bool isBoarded = Hash(variation, floor, index) % 6 == 0;
                            Patch(windowX, floorY + 3, 8, 10, CityArtCatalog.BuildingPart.HouseWindows,
                                new RectInt(isBoarded ? 8 : 0, 0, 8, 10), White);
                        }

                        index++;
                    }
                }

                // 현관문과 차양
                Patch(layout.DoorX - TilePixels / 2, y, TilePixels, DoorPatchHeight, CityArtCatalog.BuildingPart.FrontDoor,
                    new RectInt(0, 0, TilePixels, DoorPatchHeight), White);
                Fill(layout.DoorX - 7, y + DoorPatchHeight, 14, 2, Outline);
                Fill(layout.DoorX - 6, y + DoorPatchHeight + 1, 12, 1, new Color32(170, 168, 160, 255));
            }

            // 처마 그림자 후 형태별 지붕 배치
            Fill(x, y + wall - 2, width, 2, EaveShadow);

            if (isVilla)
            {
                DrawFlatRoof(x, y + wall, width, layout.Roof, FlatRoofTints[variation % FlatRoofTints.Length], true, 1);
            }
            else
            {
                DrawHipRoof(x, y + wall, width, layout.Roof, HouseRoofTints[(variation >> 4) % HouseRoofTints.Length]);
            }
        }


        // 1층 진열창과 업종 간판 및 상층 창과 옥상 설비 조합
        private void ComposeShop()
        {
            // 외벽과 우측 면 및 1층 진열창과 출입문 배치
            RectInt body = layout.Body;
            int x = body.x;
            int y = body.y;
            int width = body.width;
            int variation = style.Variation;
            Color32 wallTint = ShopWallTints[variation % ShopWallTints.Length];
            Fill(x - 1, y, width + 2, layout.Wall, Outline);
            Patch(x, y, width, layout.Wall, CityArtCatalog.BuildingPart.PlasterWall, FullTile, wallTint);
            Patch(x + width - SideFaceWidth, y, SideFaceWidth, layout.Wall, CityArtCatalog.BuildingPart.PlasterWall, FullTile,
                Darken(wallTint, 80));
            Patch(x + 2, y, width - 5, StorefrontHeight, CityArtCatalog.BuildingPart.Storefront,
                new RectInt(0, 0, TilePixels, StorefrontHeight), White);
            Fill(x + width - 4, y, 1, StorefrontHeight, new Color32(62, 66, 74, 255));
            Patch(layout.DoorX - TilePixels / 2, y, TilePixels, DoorPatchHeight, CityArtCatalog.BuildingPart.ShopDoor,
                new RectInt(0, 0, TilePixels, DoorPatchHeight), White);

            // 음식점과 카페 및 마트만 업종색 차양 표시
            if (style.HasAwning)
            {
                Patch(x + 1, y + StorefrontHeight - 1, width - 2, 6, CityArtCatalog.BuildingPart.Awning,
                    new RectInt(0, 10, TilePixels, 6), style.Accent);
                Fill(x + 1, y + StorefrontHeight + 5, width - 2, 1, Outline);
            }
            else
            {
                Fill(x + 2, y + StorefrontHeight, width - 5, 6, Darken(wallTint, 90));
            }

            DrawSign(x, y + ShopSignOffset, width);

            // 상층 알루미늄 창과 난간 마감
            for (int floor = 0; floor < layout.Floors; floor++)
            {
                int floorY = y + ShopSignOffset + SignHeight + 1 + floor * FloorHeight;

                for (int windowX = x + 4; windowX + 14 <= x + width - 6; windowX += 20)
                {
                    Patch(windowX, floorY + 3, 14, 10, CityArtCatalog.BuildingPart.UpperWindow, new RectInt(1, 0, 14, 10), White);
                }
            }

            Fill(x, y + layout.Wall - ShopParapetHeight, width, ShopParapetHeight, wallTint);
            Fill(x, y + layout.Wall - ShopParapetHeight, width, 1, Darken(wallTint, 70));
            DrawFlatRoof(x, y + layout.Wall, width, layout.Roof, FlatRoofTints[(variation >> 2) % FlatRoofTints.Length], true, 2);
        }


        // 공장 본동과 사무동 및 보조 창고와 컨테이너 조합
        private void ComposeIndustrial()
        {
            // 앞쪽에 놓이는 시설일수록 나중에 그려 겹침 순서 유지
            RectInt body = layout.Body;
            bool isFactory = style.Form == CityArtCatalog.BuildingForm.Factory;
            DrawShed(body.x, body.y, body.width, layout.Wall, layout.Roof, style.Variation, isFactory, layout.DoorX,
                style.Sign, layout.Office);

            if (layout.Shed.width > 0)
            {
                DrawShed(layout.Shed.x, layout.Shed.y, layout.Shed.width, ShedWallHeight, ShedRoofHeight, style.Variation + 1, false,
                    layout.Shed.x + layout.Shed.width / 2, CityArtCatalog.BuildingPart.None, default);
            }

            if (layout.Office.width > 0)
            {
                DrawOffice(layout.Office);
            }

            for (int i = 0; i < layout.ContainerCount; i++)
            {
                // 두 단으로 쌓는 컨테이너의 색상은 건물 변형값에서 고정
                int containerX = layout.ContainerOrigin.x + i / 2 * ContainerSpacing;
                int containerY = layout.ContainerOrigin.y + i % 2 * ContainerHeight;
                Fill(containerX - 1, containerY - 1, ContainerWidth + 2, ContainerHeight + 2, Outline);
                Patch(containerX, containerY, ContainerWidth, ContainerHeight, CityArtCatalog.BuildingPart.Container,
                    new RectInt(0, 0, TilePixels, ContainerHeight), ContainerTints[Hash(style.Variation, i, 0) % ContainerTints.Length]);
                Fill(containerX + ContainerWidth - 1, containerY + 1, 1, ContainerHeight - 2, Outline);
            }
        }


        // 금속 외벽과 셔터 및 띠창과 지붕의 공장동 조합
        private void DrawShed(int x, int y, int width, int wall, int roof, int variation, bool isFactory, int doorX,
            CityArtCatalog.BuildingPart sign, RectInt office)
        {
            // 외벽과 우측 면 및 콘크리트 기단 배치
            Color32 metal = MetalWallTints[variation % MetalWallTints.Length];
            Fill(x - 1, y, width + 2, wall, Outline);
            Patch(x, y, width, wall, CityArtCatalog.BuildingPart.MetalWall, FullTile, metal);
            Patch(x + width - 4, y, 4, wall, CityArtCatalog.BuildingPart.MetalWall, FullTile, Darken(metal, 78));
            Fill(x, y, width, 3, new Color32(140, 140, 136, 255));

            // 높은 벽은 띠창을 여러 줄로 반복
            for (int bandY = wall - 14; bandY > 30; bandY -= BandWindowSpacing)
            {
                Patch(x + 4, y + bandY, width - 12, BandWindowHeight, CityArtCatalog.BuildingPart.ShutterBand,
                    new RectInt(0, 10, TilePixels, BandWindowHeight), White);
            }

            // 출입구 셔터와 긴 벽의 추가 셔터를 사무동과 겹치지 않게 배치
            int count = Mathf.Max(1, width / ShedSpacing);
            var shutters = new List<int> { doorX };

            for (int i = 0; i < count; i++)
            {
                int candidate = x + (i + 1) * width / (count + 1);
                bool isSeparated = true;

                foreach (int placed in shutters)
                {
                    isSeparated &= Mathf.Abs(candidate - placed) > 30;
                }

                if (isSeparated)
                {
                    shutters.Add(candidate);
                }
            }

            foreach (int candidate in shutters)
            {
                int center = Mathf.Clamp(candidate, x + 13, x + width - 15);

                if (office.width > 0 && center > office.xMin - ShutterWidth / 2 && center < office.xMax + ShutterWidth / 2)
                {
                    continue;
                }

                Fill(center - ShutterWidth / 2, y, ShutterWidth, ShutterHeight, Outline);
                Patch(center - 11, y, 22, 23, CityArtCatalog.BuildingPart.ShutterBand, new RectInt(0, 0, TilePixels, 9), White);
                Fill(center - 11, y + 23, 22, 3, new Color32(88, 92, 98, 255));
            }

            // 정비소와 공구점 계열만 업종 간판 표시
            int signWidth = Mathf.Min(72, width - 40);

            if (sign != CityArtCatalog.BuildingPart.None && signWidth >= 40)
            {
                int signX = x + width - signWidth - 10;
                Fill(signX - 1, y + 29, signWidth + 2, SignHeight + 2, Outline);
                Patch(signX, y + 30, signWidth, SignHeight, CityArtCatalog.BuildingPart.SignBand,
                    new RectInt(0, 0, TilePixels, SignHeight), style.Accent);
                Patch(signX + 2, y + 30, TilePixels, SignHeight, sign, new RectInt(0, 3, TilePixels, SignHeight), White);
                Patch(signX + 20, y + 33, Mathf.Max(0, (signWidth - 24) / GlyphSpacing * GlyphSpacing - 3), 5,
                    CityArtCatalog.BuildingPart.SignBand, new RectInt(0, 10, GlyphSpacing, 5), White);
            }

            Fill(x, y + wall - 2, width, 2, EaveShadow);
            int roofY = y + wall;

            // 공장은 톱날 지붕과 굴뚝, 창고는 완만한 금속 박공지붕
            if (isFactory)
            {
                int flat = roof - SawToothHeight;
                Fill(x - 1, roofY, width + 2, flat + 1, Outline);
                Patch(x, roofY, width, flat, CityArtCatalog.BuildingPart.MetalWall, FullTile, new Color32(150, 158, 166, 255));
                Fill(x, roofY, width, 1, new Color32(96, 100, 108, 255));
                Patch(x - 1, roofY + flat, width + 2, TilePixels, CityArtCatalog.BuildingPart.SawRoof, FullTile, White);
                int stackX = x + width - 30;
                int top = roofY + roof + CityArtCatalog.FactoryStackRise;
                Patch(stackX - 8, roofY, TilePixels, top - roofY - 4, CityArtCatalog.BuildingPart.Smokestack,
                    new RectInt(0, 0, TilePixels, 12), White);
                Patch(stackX - 8, top - 4, TilePixels, 4, CityArtCatalog.BuildingPart.Smokestack, new RectInt(0, 12, TilePixels, 4), White);
            }
            else
            {
                DrawMetalRoof(x, roofY, width, roof, MetalRoofTints[(variation >> 2) % MetalRoofTints.Length]);
            }
        }


        // 공장 앞 2층 사무동 조합
        private void DrawOffice(RectInt area)
        {
            // 흰 외벽과 중앙 유리문 및 층별 창과 평지붕 배치
            Color32 wallTint = new Color32(240, 240, 236, 255);
            int wall = OfficeWall;
            Fill(area.x - 1, area.y, area.width + 2, wall, Outline);
            Patch(area.x, area.y, area.width, wall, CityArtCatalog.BuildingPart.PlasterWall, FullTile, wallTint);
            Patch(area.xMax - SideFaceWidth, area.y, SideFaceWidth, wall, CityArtCatalog.BuildingPart.PlasterWall, FullTile,
                Darken(wallTint, 80));
            int doorX = area.x + area.width / 2;
            Patch(doorX - TilePixels / 2, area.y, TilePixels, DoorPatchHeight, CityArtCatalog.BuildingPart.ShopDoor,
                new RectInt(0, 0, TilePixels, DoorPatchHeight), White);

            for (int floor = 0; floor < 2; floor++)
            {
                for (int windowX = area.x + 4; windowX + 14 <= area.xMax - 5; windowX += 18)
                {
                    if (floor == 0 && Mathf.Abs(windowX + 7 - doorX) < 14)
                    {
                        continue;
                    }

                    Patch(windowX, area.y + 5 + floor * FloorHeight, 14, 10, CityArtCatalog.BuildingPart.UpperWindow,
                        new RectInt(1, 0, 14, 10), White);
                }
            }

            Fill(area.x, area.y + wall - 2, area.width, 2, EaveShadow);
            DrawFlatRoof(area.x, area.y + wall, area.width, area.height - wall, FlatRoofTints[1], false, 1);
        }


        // 업종색 간판 바탕과 아이콘 및 한글형 글자 줄 배치
        private void DrawSign(int x, int y, int width)
        {
            // 아이콘 오른쪽의 글자 줄은 글자 묶음 단위로 잘라 끊긴 글자 방지
            Fill(x, y - 1, width, SignHeight + 2, Outline);
            Patch(x + 1, y, width - 2, SignHeight, CityArtCatalog.BuildingPart.SignBand, new RectInt(0, 0, TilePixels, SignHeight), style.Accent);

            if (style.Sign == CityArtCatalog.BuildingPart.None)
            {
                return;
            }

            Patch(x + 3, y, TilePixels, SignHeight, style.Sign, new RectInt(0, 3, TilePixels, SignHeight), White);
            int glyphWidth = Mathf.Max(0, (width - 26) / GlyphSpacing * GlyphSpacing - 3);
            Patch(x + 21, y + 3, glyphWidth, 5, CityArtCatalog.BuildingPart.SignBand, new RectInt(0, 10, GlyphSpacing, 5), White);
        }


        // 처마와 기와 및 양끝 경사를 행 단위로 조합한 우진각 지붕
        private void DrawHipRoof(int x, int y, int width, int rows, Color32 tint)
        {
            // 처마 아래 행부터 위로 갈수록 양끝을 한 픽셀씩 줄여 경사 표현
            Color32 dark = Darken(tint, 74);

            for (int row = 0; row < rows; row++)
            {
                int inset = Mathf.Max(0, row - EaveRows);
                int left = x - RoofOverhang + inset;
                int right = x + width + RoofOverhang - inset;
                var tileRow = new RectInt(0, (rows - 1 - row) % TilePixels, TilePixels, 1);

                if (row < EaveRows)
                {
                    Patch(left, y + row, right - left, 1, CityArtCatalog.BuildingPart.RoofEdge,
                        new RectInt(0, TilePixels - EaveRows + row, TilePixels, 1), tint);
                }
                else if (row == rows - 1)
                {
                    Patch(left, y + row, right - left, 1, CityArtCatalog.BuildingPart.RoofEdge, new RectInt(0, 9, TilePixels, 1), tint);
                }
                else
                {
                    Patch(left, y + row, right - left, 1, CityArtCatalog.BuildingPart.RoofTiles, tileRow, tint);
                    Patch(left, y + row, HipShadeWidth, 1, CityArtCatalog.BuildingPart.RoofTiles, tileRow, dark);
                    Patch(right - HipShadeWidth, y + row, HipShadeWidth, 1, CityArtCatalog.BuildingPart.RoofTiles, tileRow, dark);
                }

                Fill(left - 1, y + row, 1, 1, Outline);
                Fill(right, y + row, 1, 1, Outline);
            }

            int topInset = Mathf.Max(0, rows - EaveRows);
            Fill(x - RoofOverhang + topInset - 1, y + rows, width + RoofOverhang * 2 - topInset * 2 + 2, 1, Outline);
        }


        // 완만한 경사의 골강판 박공지붕
        private void DrawMetalRoof(int x, int y, int width, int rows, Color32 tint)
        {
            // 네 행마다 한 픽셀씩 줄이는 낮은 경사와 상단 용마루 배치
            for (int row = 0; row < rows; row++)
            {
                int inset = row / MetalRoofSlopeRows;
                int left = x - RoofOverhang + inset;
                int right = x + width + RoofOverhang - inset;

                if (row == 0)
                {
                    Fill(left, y, right - left, 1, Darken(tint, 70));
                }
                else if (row == rows - 1)
                {
                    Patch(left, y + row, right - left, 1, CityArtCatalog.BuildingPart.RoofEdge, new RectInt(0, 10, TilePixels, 1), tint);
                }
                else
                {
                    Patch(left, y + row, right - left, 1, CityArtCatalog.BuildingPart.MetalWall,
                        new RectInt(0, row % TilePixels, TilePixels, 1), tint);
                }

                Fill(left - 1, y + row, 1, 1, Outline);
                Fill(right, y + row, 1, 1, Outline);
            }

            int topInset = (rows - 1) / MetalRoofSlopeRows;
            Fill(x - RoofOverhang - 1 + topInset, y + rows, width + RoofOverhang * 2 + 2 - topInset * 2, 1, Outline);
        }


        // 방수 도장 옥상과 난간 및 물탱크와 실외기 배치
        private void DrawFlatRoof(int x, int y, int width, int rows, Color32 tint, bool hasTank, int units)
        {
            // 옥상 바닥과 앞뒤 및 좌우 난간 테두리
            Fill(x - 1, y, width + 2, rows + 1, Outline);
            Patch(x, y, width, rows, CityArtCatalog.BuildingPart.FlatRoof, FullTile, tint);
            Fill(x, y, width, 3, RoofRim);
            Fill(x, y, width, 1, new Color32(150, 148, 142, 255));
            Fill(x, y + rows - 2, width, 2, RoofRim);
            Fill(x, y, 2, rows, RoofRim);
            Fill(x + width - 2, y, 2, rows, Darken(RoofRim, 82));

            // 넓은 옥상만 물탱크와 실외기 배치
            if (hasTank && width >= 40 && rows >= 13)
            {
                Patch(x + width - 18, y + 3, TilePixels, 12, CityArtCatalog.BuildingPart.RoofTank, new RectInt(0, 0, TilePixels, 12), White);
            }

            int unitX = x + 6;

            for (int i = 0; i < units && unitX + 12 <= x + width - 20; i++)
            {
                Patch(unitX - 2, y + 4, TilePixels, 9, CityArtCatalog.BuildingPart.RoofUnit, new RectInt(0, 0, TilePixels, 9), White);
                unitX += 18;
            }
        }


        // 필지 앞 블록 담장과 측면 담장 및 실제 출입 방향의 대문 배치
        private void DrawFence(Color32 tint)
        {
            // 남북 출입은 앞뒤 담장 대문, 동서 출입은 측면 담장 틈으로 표시
            var fenceSection = new RectInt(0, 8, TilePixels, FenceHeight);
            var sideSection = new RectInt(0, 0, FenceSideWidth, FenceHeight);
            int lotWidth = layout.LotWidth;
            int fenceY = layout.FenceY;
            int gateX = layout.DoorX;

            if (layout.Side == DoorSide.South || layout.Side == DoorSide.North)
            {
                Patch(0, fenceY, gateX - GateWidth / 2, FenceHeight, CityArtCatalog.BuildingPart.BlockFence, fenceSection, tint);
                Patch(gateX + GateWidth / 2, fenceY, lotWidth - gateX - GateWidth / 2, FenceHeight,
                    CityArtCatalog.BuildingPart.BlockFence, fenceSection, tint);

                if (style.Form == CityArtCatalog.BuildingForm.Factory || style.Form == CityArtCatalog.BuildingForm.Warehouse)
                {
                    Fill(gateX - 9, fenceY, 2, GatePostHeight, SafetyYellow);
                    Fill(gateX + 7, fenceY, 2, GatePostHeight, SafetyYellow);
                }
                else
                {
                    Patch(gateX - GateWidth / 2, fenceY, GateWidth, GatePostHeight, CityArtCatalog.BuildingPart.Gate,
                        new RectInt(0, 0, TilePixels, GatePostHeight), White);
                }
            }
            else
            {
                Patch(0, fenceY, lotWidth, FenceHeight, CityArtCatalog.BuildingPart.BlockFence, fenceSection, tint);
            }

            int sideLength = layout.SideTo - layout.SideFrom;

            if (sideLength <= 0)
            {
                return;
            }

            for (int side = 0; side < 2; side++)
            {
                // 출입 방향 측면은 대문 폭만큼 비워 통로 표시
                int sideX = side == 0 ? 0 : lotWidth - FenceSideWidth;
                bool hasGate = side == 0 ? layout.Side == DoorSide.West : layout.Side == DoorSide.East;

                if (hasGate)
                {
                    Patch(sideX, layout.SideFrom, FenceSideWidth, layout.GateY - layout.SideFrom,
                        CityArtCatalog.BuildingPart.BlockFence, sideSection, tint);
                    Patch(sideX, layout.GateY + GateWidth, FenceSideWidth, layout.SideTo - layout.GateY - GateWidth,
                        CityArtCatalog.BuildingPart.BlockFence, sideSection, tint);
                }
                else
                {
                    Patch(sideX, layout.SideFrom, FenceSideWidth, sideLength, CityArtCatalog.BuildingPart.BlockFence, sideSection, tint);
                }
            }
        }


        // 정면 형태의 마당 바닥과 진입로 및 본체 그림자를 지형 버퍼에 합성
        private static void PaintFrontGround(CityArtCatalog art, FrontLayout layout, CityArtCatalog.BuildingStyle style,
            NativeArray<Color32> pixels, int stride, Vector2Int origin, Vector2Int worldOffset, float scale)
        {
            // 필지 원점 기준 영역을 출력 좌표로 옮기고 질감 위상은 전역 픽셀에 고정
            RectInt yard = Offset(layout.Yard, origin);

            switch (style.Form)
            {
                case CityArtCatalog.BuildingForm.House:
                case CityArtCatalog.BuildingForm.Villa:
                    if (style.Variation % 3 != 0)
                    {
                        PaintSurface(art, pixels, stride, yard, scale, worldOffset, CityArtCatalog.BuildingPart.YardPaving, YardCement);
                    }
                    else
                    {
                        // 잔디 마당은 대문에서 현관까지 포장 진입로만 표시
                        foreach (RectInt path in layout.GetPath())
                        {
                            PaintSurface(art, pixels, stride, Offset(path, origin), scale, worldOffset,
                                CityArtCatalog.BuildingPart.YardPaving, YardCement);
                        }
                    }

                    break;
                case CityArtCatalog.BuildingForm.Shop:
                    // 앞 주차장과 출입문 앞을 비운 주차선만 표시하고 지붕 뒤 필지는 기존 지형 유지
                    PaintSurface(art, pixels, stride, yard, scale, worldOffset, CityArtCatalog.BuildingPart.Asphalt, White);

                    for (int lineX = layout.Body.x + 2; lineX < layout.Body.xMax; lineX += ParkingLineSpacing)
                    {
                        if (Mathf.Abs(lineX - layout.DoorX) > 12)
                        {
                            PaintColor(pixels, stride, new RectInt(origin.x + lineX, yard.y + 3, 1, Mathf.Max(0, yard.height - 6)),
                                scale, ParkingLine);
                        }
                    }

                    break;
                default:
                    PaintSurface(art, pixels, stride, new RectInt(origin.x, origin.y, layout.LotWidth, layout.LotDepth), scale,
                        worldOffset, CityArtCatalog.BuildingPart.YardPaving, IndustrialConcrete);
                    break;
            }

            // 본체와 부속 건물의 우하단 그림자
            Shade(pixels, stride, Offset(Shadow(layout.Body), origin), scale, BodyShadowShade);

            if (layout.Office.width > 0)
            {
                Shade(pixels, stride, Offset(Shadow(layout.Office), origin), scale, BodyShadowShade);
            }

            if (layout.Shed.width > 0)
            {
                Shade(pixels, stride, Offset(Shadow(layout.Shed), origin), scale, BodyShadowShade);
            }
        }


        // 본체 영역을 우하단으로 옮긴 그림자 영역 계산
        private static RectInt Shadow(RectInt area)
        {
            // 좌상단 광원 기준의 고정 오프셋 적용
            return new RectInt(area.x + BodyShadowOffset, area.y - BodyShadowOffset, area.width, area.height);
        }


        // 필지 원점 기준 영역을 출력 버퍼 좌표로 이동
        private static RectInt Offset(RectInt area, Vector2Int origin)
        {
            // 크기는 유지하고 위치만 이동
            return new RectInt(area.x + origin.x, area.y + origin.y, area.width, area.height);
        }


        // 전역 픽셀 위상의 건물 조각 질감을 지형 버퍼 영역에 기록
        private static void PaintSurface(CityArtCatalog art, NativeArray<Color32> pixels, int stride, RectInt area, float scale,
            Vector2Int worldOffset, CityArtCatalog.BuildingPart part, Color32 tint)
        {
            // 구역 경계에서도 이어지도록 출력 픽셀 중심을 전역 원본 픽셀로 환산
            RectInt region = art.GetBuildingRegion(part, FullTile);
            int minX = Mathf.Max(0, Mathf.CeilToInt(area.x * scale - .5f));
            int minY = Mathf.Max(0, Mathf.CeilToInt(area.y * scale - .5f));
            int maxX = Mathf.Min(stride, Mathf.CeilToInt(area.xMax * scale - .5f));
            int maxY = Mathf.Min(stride, Mathf.CeilToInt(area.yMax * scale - .5f));

            for (int y = minY; y < maxY; y++)
            {
                for (int x = minX; x < maxX; x++)
                {
                    int sourceX = Mathf.FloorToInt((x + .5f) / scale) + worldOffset.x;
                    int sourceY = Mathf.FloorToInt((y + .5f) / scale) + worldOffset.y;
                    Color32 color = art.GetBuildingPixel(region.x + sourceX % region.width, region.y + sourceY % region.height);
                    pixels[y * stride + x] = new Color32((byte)(color.r * tint.r / 255), (byte)(color.g * tint.g / 255),
                        (byte)(color.b * tint.b / 255), 255);
                }
            }
        }


        // 지형 버퍼 영역을 단색으로 기록
        private static void PaintColor(NativeArray<Color32> pixels, int stride, RectInt area, float scale, Color32 color)
        {
            // 그림자와 같은 픽셀 중심 규칙으로 출력 범위 제한
            int minX = Mathf.Max(0, Mathf.CeilToInt(area.x * scale - .5f));
            int minY = Mathf.Max(0, Mathf.CeilToInt(area.y * scale - .5f));
            int maxX = Mathf.Min(stride, Mathf.CeilToInt(area.xMax * scale - .5f));
            int maxY = Mathf.Min(stride, Mathf.CeilToInt(area.yMax * scale - .5f));

            for (int y = minY; y < maxY; y++)
            {
                for (int x = minX; x < maxX; x++)
                {
                    pixels[y * stride + x] = color;
                }
            }
        }


        // 크기가 있는 사각형만 조각 배치로 전달
        private void Patch(int x, int y, int width, int height, CityArtCatalog.BuildingPart part, RectInt section, Color32 tint)
        {
            // 좁은 필지 계산에서 생기는 빈 사각형의 불필요한 정점 생성 방지
            if (width <= 0 || height <= 0)
            {
                return;
            }

            Patch(new RectInt(x, y, width, height), part, section, tint);
        }


        // 흰색 단색 조각을 색조로 칠한 사각형 배치
        private void Fill(int x, int y, int width, int height, Color32 color)
        {
            // 외곽선과 단색 면을 같은 메쉬 재질로 표현
            Patch(x, y, width, height, CityArtCatalog.BuildingPart.Solid, FullTile, color);
        }


        // 색상 채널을 백분율로 어둡게 변환
        private static Color32 Darken(Color32 color, int percent)
        {
            // 알파는 유지하고 RGB만 같은 비율로 조정
            return new Color32((byte)(color.r * percent / 100), (byte)(color.g * percent / 100), (byte)(color.b * percent / 100), color.a);
        }


        // 창과 컨테이너 변형 선택용 정수 해시
        private static int Hash(int a, int b, int c)
        {
            // 생성 난수를 소비하지 않는 고정 입력 혼합
            uint value = unchecked((uint)a * 73856093u ^ (uint)b * 19349663u ^ (uint)c * 83492791u);
            value ^= value >> 13;
            value = unchecked(value * 0x7feb352du);
            value ^= value >> 15;
            return (int)(value & 0x7fffffffu);
        }


        // 동일한 조각 배치를 메쉬 또는 축소 지도 출력으로 전달
        private void Patch(RectInt target, CityArtCatalog.BuildingPart part, RectInt section, Color32 tint)
        {
            // 출력 사각형 크기와 원본 반복 영역을 분리해 고정 이미지 확대 방지
            RectInt region = art.GetBuildingRegion(part, section);

            if (pixels.IsCreated)
            {
                RasterPatch(target, region, tint);
                return;
            }

            int first = vertices.Count;
            vertices.Add(new Vector3(target.xMin / (float)TilePixels, target.yMin / (float)TilePixels));
            vertices.Add(new Vector3(target.xMax / (float)TilePixels, target.yMin / (float)TilePixels));
            vertices.Add(new Vector3(target.xMax / (float)TilePixels, target.yMax / (float)TilePixels));
            vertices.Add(new Vector3(target.xMin / (float)TilePixels, target.yMax / (float)TilePixels));
            coordinates.Add(Vector2.zero);
            coordinates.Add(new Vector2(target.width, 0));
            coordinates.Add(new Vector2(target.width, target.height));
            coordinates.Add(new Vector2(0, target.height));

            for (int corner = 0; corner < 4; corner++)
            {
                regions.Add(new Vector4(region.x, region.y, region.width, region.height));
                colors.Add(tint);
            }

            triangles.Add(first);
            triangles.Add(first + 1);
            triangles.Add(first + 2);
            triangles.Add(first);
            triangles.Add(first + 2);
            triangles.Add(first + 3);
        }


        // 원본 조각 반복 규칙을 축소 지도 픽셀 중심에 샘플링
        private void RasterPatch(RectInt target, RectInt region, Color32 tint)
        {
            // 출력 버퍼 안의 실제 픽셀만 조회해 전체 지도용 대형 원본 생성 방지
            int minX = Mathf.Max(0, Mathf.CeilToInt((origin.x + target.x) * scale - .5f));
            int minY = Mathf.Max(0, Mathf.CeilToInt((origin.y + target.y) * scale - .5f));
            int maxX = Mathf.Min(stride, Mathf.CeilToInt((origin.x + target.xMax) * scale - .5f));
            int maxY = Mathf.Min(stride, Mathf.CeilToInt((origin.y + target.yMax) * scale - .5f));

            for (int y = minY; y < maxY; y++)
            {
                for (int x = minX; x < maxX; x++)
                {
                    int localX = Mathf.FloorToInt((x + .5f) / scale) - origin.x - target.x;
                    int localY = Mathf.FloorToInt((y + .5f) / scale) - origin.y - target.y;
                    Color32 color = art.GetBuildingPixel(region.x + localX % region.width, region.y + localY % region.height);

                    if (color.a != 0)
                    {
                        pixels[y * stride + x] = new Color32((byte)(color.r * tint.r / 255),
                            (byte)(color.g * tint.g / 255), (byte)(color.b * tint.b / 255), 255);
                    }
                }
            }
        }


        // 그림자 사각형 안의 기존 지형 색상을 일정 비율로 어둡게 합성
        private static void Shade(NativeArray<Color32> pixels, int stride, RectInt area, float scale, int shade)
        {
            // 구역 경계를 넘는 그림자도 같은 전역 원본 좌표에서 제한
            int minX = Mathf.Max(0, Mathf.CeilToInt(area.x * scale - .5f));
            int minY = Mathf.Max(0, Mathf.CeilToInt(area.y * scale - .5f));
            int maxX = Mathf.Min(stride, Mathf.CeilToInt(area.xMax * scale - .5f));
            int maxY = Mathf.Min(stride, Mathf.CeilToInt(area.yMax * scale - .5f));

            for (int y = minY; y < maxY; y++)
            {
                for (int x = minX; x < maxX; x++)
                {
                    Color32 color = pixels[y * stride + x];
                    pixels[y * stride + x] = new Color32((byte)(color.r * shade / 255),
                        (byte)(color.g * shade / 255), (byte)(color.b * shade / 255), color.a);
                }
            }
        }


        /// <summary>
        /// 출입문이 놓인 필지 바깥 방향
        /// </summary>
        private enum DoorSide
        {
            South, North, West, East
        }

        /// <summary>
        /// 정면 시점 건물의 필지 안 본체와 마당 및 담장과 부속 시설 배치 계산
        /// </summary>
        private readonly struct FrontLayout
        {
            // 필지 크기와 본체 영역 및 벽과 지붕 높이
            internal readonly int LotWidth;
            internal readonly int LotDepth;
            internal readonly RectInt Body;
            internal readonly int Wall;
            internal readonly int Roof;
            internal readonly int Floors;

            // 출입 방향과 정면 투영 출입 위치
            internal readonly DoorSide Side;
            internal readonly int DoorX;
            internal readonly int DoorY;

            // 마당과 담장 및 측면 대문 범위
            internal readonly RectInt Yard;
            internal readonly int FenceY;
            internal readonly int SideFrom;
            internal readonly int SideTo;
            internal readonly int GateY;

            // 공장 부속 사무동과 보조 창고 및 컨테이너 배치
            internal readonly RectInt Office;
            internal readonly RectInt Shed;
            internal readonly Vector2Int ContainerOrigin;
            internal readonly int ContainerCount;


            // 형태별 층수와 필지 깊이에서 본체와 마당 배치 계산
            internal FrontLayout(CityBuilding building, CityArtCatalog.BuildingStyle style)
            {
                // 기존 형태는 계산 없이 빈 배치 유지
                this = default;

                if (style.Form == CityArtCatalog.BuildingForm.Legacy)
                {
                    return;
                }

                RectInt bounds = building.Bounds;
                LotWidth = bounds.width * TilePixels;
                LotDepth = bounds.height * TilePixels;
                Side = building.Door.y < bounds.yMin ? DoorSide.South : building.Door.y >= bounds.yMax ? DoorSide.North
                    : building.Door.x < bounds.xMin ? DoorSide.West : DoorSide.East;

                // 형태별 여백과 기본 벽 높이 및 최소 층수 선택
                int inset;
                int baseHeight;
                int topHeight = 0;
                int floors;
                int minimumFloors;
                int frontYard;

                switch (style.Form)
                {
                    case CityArtCatalog.BuildingForm.House:
                        inset = 6;
                        baseHeight = HouseBaseHeight;
                        floors = HouseFloors;
                        minimumFloors = 1;
                        frontYard = 28;
                        break;
                    case CityArtCatalog.BuildingForm.Villa:
                        inset = 6;
                        baseHeight = 0;
                        floors = VillaFloors;
                        minimumFloors = 1;
                        frontYard = 24;
                        break;
                    case CityArtCatalog.BuildingForm.Shop:
                        inset = 3;
                        baseHeight = ShopGroundHeight;
                        topHeight = ShopParapetHeight;
                        floors = LotDepth < 112 ? 1 : LotDepth < 160 ? 2 : 3;
                        minimumFloors = 0;
                        frontYard = 22;
                        break;
                    default:
                        // 공장 벽은 필지 깊이에 비례하되 최소 마당과 셔터 높이 범위로 제한
                        inset = 8;
                        baseHeight = Mathf.Clamp((LotDepth * IndustrialWallPercent / 100 + 8) / 16 * 16,
                            MinimumIndustrialWall, MaximumIndustrialWall);
                        baseHeight = Mathf.Max(ShutterHeight + 4, Mathf.Min(baseHeight, (LotDepth - MinimumYard) * 3 / 4));
                        floors = 0;
                        minimumFloors = 0;
                        frontYard = LotDepth;
                        break;
                }

                // 벽 3 대 지붕 1 비율을 유지하며 최소 마당이 남도록 층수 축소
                Wall = baseHeight + floors * FloorHeight + topHeight;
                Roof = (Wall + 1) / 3;

                while (Wall + Roof + MinimumYard > LotDepth && floors > minimumFloors)
                {
                    floors--;
                    Wall = baseHeight + floors * FloorHeight + topHeight;
                    Roof = (Wall + 1) / 3;
                }

                Floors = floors;
                int bodyHeight = Wall + Roof;
                int bodyY = Side == DoorSide.North ? 0 : Mathf.Max(0, Mathf.Min(frontYard, LotDepth - bodyHeight));
                Body = new RectInt(inset, bodyY, LotWidth - inset * 2, bodyHeight);

                // 실제 Door 좌표를 정면 벽의 출입문 위치로 투영
                DoorX = Mathf.Clamp((building.Door.x - bounds.x) * TilePixels + TilePixels / 2, Body.x + 8, Body.xMax - 8);
                DoorY = (building.Door.y - bounds.y) * TilePixels + TilePixels / 2;

                // 북쪽 출입은 본체를 앞에 두고 뒤편을 마당으로 사용
                if (Side == DoorSide.North)
                {
                    Yard = new RectInt(0, Body.yMax, LotWidth, LotDepth - Body.yMax);
                    FenceY = LotDepth - FenceHeight;
                    SideFrom = Body.yMax;
                    SideTo = FenceY;
                }
                else
                {
                    Yard = new RectInt(0, 0, LotWidth, Body.y);
                    FenceY = 0;
                    SideFrom = FenceHeight;
                    SideTo = Body.y;
                }

                GateY = Mathf.Max(SideFrom + 2, Mathf.Min(SideTo - GateWidth - 2, DoorY - GateWidth / 2));

                if ((style.Form != CityArtCatalog.BuildingForm.Factory && style.Form != CityArtCatalog.BuildingForm.Warehouse) ||
                    Side == DoorSide.North)
                {
                    return;
                }

                // 넓은 공장은 앞마당에 사무동, 깊은 마당은 보조 창고와 컨테이너 배치
                int yardDepth = Yard.height;

                if (Body.width >= OfficeMinimumBodyWidth && yardDepth >= OfficeMinimumYard)
                {
                    Office = new RectInt(Body.x + 10, Body.y - OfficeOverlap, OfficeWidth, OfficeWall + (OfficeWall + 1) / 3);
                }

                int yardRight = Body.xMax;

                if (yardDepth >= ShedMinimumYard && Body.width >= ShedMinimumBodyWidth)
                {
                    int shedWidth = Body.width / 2 - 8;
                    Shed = new RectInt(Body.xMax - shedWidth, FenceHeight + 40, shedWidth, ShedWallHeight + ShedRoofHeight);
                    yardRight = Shed.x - 8;
                }

                if (yardDepth >= ContainerMinimumYard)
                {
                    int span = yardRight - Body.x;
                    ContainerCount = span >= 200 ? 4 : span >= 120 ? 3 : 1;
                    ContainerOrigin = new Vector2Int(yardRight - ContainerSpacing * ((ContainerCount + 1) / 2) - 4, FenceHeight + 8);
                }
            }


            // 잔디 마당에서 대문부터 현관까지 이어지는 진입로 영역 조회
            internal RectInt[] GetPath()
            {
                // 남북 출입은 직선, 동서 출입은 측면 대문에서 꺾이는 진입로
                const int halfWidth = 5;

                if (Side == DoorSide.South || Side == DoorSide.North)
                {
                    return new[] { new RectInt(DoorX - halfWidth, Yard.y, halfWidth * 2, Yard.height) };
                }

                int sideX = Side == DoorSide.West ? 0 : DoorX;
                int length = Side == DoorSide.West ? DoorX + halfWidth : LotWidth - DoorX;
                return new[]
                {
                    new RectInt(sideX, GateY + 3, length, halfWidth * 2),
                    new RectInt(DoorX - halfWidth, GateY + 3, halfWidth * 2, Mathf.Max(0, Body.y - GateY - 3))
                };
            }
        }
    }
}
