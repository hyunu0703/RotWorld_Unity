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

        // 조합할 건물과 카탈로그 및 변경하지 않는 외형 선택
        private readonly CityArtCatalog art;
        private readonly CityBuilding building;
        private readonly CityArtCatalog.BuildingStyle style;

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
            int width = building.Bounds.width * TilePixels;
            int height = building.Bounds.height * TilePixels;
            int dx = Border + style.Height / 4;
            int dy = Border + style.Height / 3;
            Shade(pixels, stride, new RectInt(x + dx - 1, y - dy - 1, width + 2, height + 2), scale, ShadowSoftShade);
            Shade(pixels, stride, new RectInt(x + dx, y - dy, width, height), scale, ShadowMainShade);
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
    }
}
