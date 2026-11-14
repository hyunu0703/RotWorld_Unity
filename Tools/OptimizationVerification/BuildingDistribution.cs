using System;
using System.Collections.Generic;
using System.Linq;
using RotWorld;
using UnityEngine;

/// <summary>
/// 실제 건물 배정 코드의 비율과 소규모 구성 및 마을과 해안 제약 검증
/// </summary>
public static class BuildingDistribution
{
    // 승인된 종류 순서와 목표 백분율의 독립 검증 기준
    private static readonly int[][] Weights =
    {
        new[] { 30, 15, 15, 12, 8, 12, 8 },
        new[] { 40, 15, 20, 10, 15 },
        new[] { 30, 20, 15, 10, 8, 12, 5 },
        new[] { 35, 15, 15, 10, 10, 15 },
        new[] { 30, 25, 15, 10, 15, 5 },
        new[] { 40, 25, 25, 10 }
    };
    private static readonly int[] FirstType = { 1, 8, 13, 20, 26, 32 };
    private static readonly int[][] Tiers =
    {
        new[] { 0, 0, 1, 1, 2, 2, 2 },
        new[] { 0, 1, 1, 2, 2 },
        new[] { 0, 0, 1, 2, 2, 2, 2 },
        new[] { 0, 1, 1, 2, 2, 2 },
        new[] { 0, 0, 1, 2, 1, 2 },
        new[] { 0, 1, 2, 2 }
    };

    // 검증한 조건의 합계
    private static int checks;


    // 합성 생활권과 실제 생성 지도의 배분 규칙 검증
    public static void Main()
    {
        // 경계 수량과 음수 시드를 포함한 전체 구역별 수량 검증
        foreach (CityRegion region in Enum.GetValues<CityRegion>())
        {
            foreach (int count in Enumerable.Range(0, 31).Concat(new[] { 99, 100, 101 }))
            {
                foreach (int seed in new[] { 0, 7, 12345, -129, int.MinValue, int.MaxValue })
                {
                    VerifyGroup(region, count, seed);
                }
            }
        }

        VerifyCoast();
        VerifyVillages();
        VerifyGeneratedMaps();
        Console.WriteLine($"PASS building distribution checks={checks}");
    }


    // 구역과 건물 수에서 배분 검증용 데이터 구성
    private static CityMap MakeMap(CityRegion region, int count, int seed, byte terrain = CityGenerator.Grass)
    {
        // 건물 간 간섭을 피하는 고정 좌표와 지정 지형 준비
        var map = new CityMap(128, seed);
        Array.Fill(map.Regions, region);
        Array.Fill(map.Terrain, terrain);
        map.VillageCenters.Add(new Vector2Int(64, 64));

        for (int i = 0; i < count; i++)
        {
            var bounds = new RectInt(10 + i % 20 * 5, 10 + i / 20 * 5, 2, 3);
            map.Buildings.Add(new CityBuilding(bounds, new Vector2Int(bounds.x, bounds.y - 1), i % 5, new[] { i, 2 }));
        }

        return map;
    }


    // 수량 단계별 최소 구성과 정확한 백분율 및 최대 잔여 배분 검증
    private static void VerifyGroup(CityRegion region, int count, int seed)
    {
        // 해안 후보 제약이 없는 합성 지도에서 기본 배분 검사
        var map = MakeMap(region, count, seed, CityGenerator.Shallows);
        var original = map.Buildings.ToArray();
        BuildingCatalog.AssignTypes(map);
        int regionIndex = (int)region;
        var counts = new int[Weights[regionIndex].Length];

        for (int i = 0; i < count; i++)
        {
            var building = map.Buildings[i];
            int type = (int)building.Type - FirstType[regionIndex];
            Check(type >= 0 && type < counts.Length, "Wrong region type");
            counts[type]++;
            Check(building.Bounds.Equals(original[i].Bounds) && building.Door == original[i].Door &&
                building.Kind == original[i].Kind && ReferenceEquals(building.Stock, original[i].Stock), "Geometry or stock changed");
        }

        // 소규모 생활권의 대표 및 생활 및 특색 시설 구성 확인
        if (count > 0 && count <= 8)
        {
            Check(Enumerable.Range(0, counts.Length).Any(i => counts[i] > 0 && Tiers[regionIndex][i] == 0), "Missing representative");

            if (count >= 2)
            {
                Check(Enumerable.Range(0, counts.Length).Any(i => counts[i] > 0 && Tiers[regionIndex][i] == 1), "Missing living facility");
            }

            if (count <= 3)
            {
                Check(!Enumerable.Range(0, counts.Length).Any(i => counts[i] > 0 && Tiers[regionIndex][i] == 2), "Special facility in tiny group");
            }
            else
            {
                Check(Enumerable.Range(0, counts.Length).Any(i => counts[i] > 0 && Tiers[regionIndex][i] == 2), "Missing special facility");
            }
        }

        // 큰 생활권의 내림 수량과 잔여량 순위를 독립 계산해 비교
        if (count >= 9)
        {
            for (int i = 0; i < counts.Length; i++)
            {
                double target = count * Weights[regionIndex][i] / 100d;
                Check(counts[i] == Math.Floor(target) || counts[i] == Math.Ceiling(target), "Quota outside rounding bounds");

                for (int j = 0; j < counts.Length; j++)
                {
                    double other = count * Weights[regionIndex][j] / 100d;

                    if (counts[i] > Math.Floor(target) && counts[j] == Math.Floor(other))
                    {
                        Check(target - Math.Floor(target) + 1e-10 >= other - Math.Floor(other), "Wrong remainder order");
                    }
                }
            }
        }

        // 같은 입력으로 다시 배분한 결과의 재현성 확인
        var before = map.Buildings.Select(b => b.Type).ToArray();
        BuildingCatalog.AssignTypes(map);
        Check(before.SequenceEqual(map.Buildings.Select(b => b.Type)), "Unstable seeded assignment");
    }


    // 강 제외와 해안 거리 경계 및 해안 후보 부족 검증
    private static void VerifyCoast()
    {
        // 바다가 없는 지형의 해안 관리초소 생성 차단 확인
        foreach (byte terrain in new[] { CityGenerator.Grass, CityGenerator.River, CityGenerator.Sand })
        {
            var map = MakeMap(CityRegion.Natural, 100, 7, terrain);
            BuildingCatalog.AssignTypes(map);
            Check(map.Buildings.All(b => b.Type != BuildingType.CoastalOutpost), "Outpost without sea");
            Check(map.Buildings.Count(b => b.Type == BuildingType.Cabin) == 44, "Incorrect redistributed cabin ratio");
        }

        // 건물 가장자리에서 원형 거리와 12타일 경계 독립 검증
        foreach (byte water in new[] { CityGenerator.Ocean, CityGenerator.Shallows, CityGenerator.River })
        {
            foreach (var offset in new[] { new Vector2Int(12, 0), new Vector2Int(13, 0), new Vector2Int(9, 9) })
            {
                var map = MakeMap(CityRegion.Natural, 100, 7);
                map.Buildings[0] = new CityBuilding(new RectInt(50, 90, 2, 3), new Vector2Int(50, 89), 0, new int[5]);
                int x = 51 + offset.x;
                int y = 92 + offset.y;
                map.Terrain[y * map.Size + x] = water;
                BuildingCatalog.AssignTypes(map);
                bool expected = water != CityGenerator.River && offset.x * offset.x + offset.y * offset.y <= 144;
                Check(map.Buildings.Count(b => b.Type == BuildingType.CoastalOutpost) == (expected ? 1 : 0), "Coast distance or capacity violation");
            }
        }
    }


    // 마을별 소규모 구성과 다른 마을의 수량 변경에 대한 독립성 검증
    private static void VerifyVillages()
    {
        // 서로 떨어진 두 마을의 건물을 번갈아 등록
        var map = MakeMap(CityRegion.Villages, 0, 12345);
        map.VillageCenters.Clear();
        map.VillageCenters.Add(new Vector2Int(20, 20));
        map.VillageCenters.Add(new Vector2Int(100, 100));

        for (int i = 0; i < 7; i++)
        {
            var bounds = new RectInt(98 + i % 3, 98 + i / 3, 2, 3);
            map.Buildings.Add(new CityBuilding(bounds, Vector2Int.zero, 0, new int[5]));

            if (i < 2)
            {
                map.Buildings.Add(new CityBuilding(new RectInt(18 + i * 3, 18, 2, 3), Vector2Int.zero, 0, new int[5]));
            }
        }

        BuildingCatalog.AssignTypes(map);
        var firstVillage = map.Buildings.Where(b => b.Bounds.center.x < 50).Select(b => b.Type).ToArray();
        Check(firstVillage.Contains(BuildingType.CountryHouse), "Tiny village missing house");
        Check(firstVillage.Contains(BuildingType.SmallSupermarket) || firstVillage.Contains(BuildingType.VillageHall), "Tiny village missing service");
        map.Buildings.Add(new CityBuilding(new RectInt(102, 102, 2, 3), Vector2Int.zero, 0, new int[5]));
        BuildingCatalog.AssignTypes(map);
        Check(firstVillage.SequenceEqual(map.Buildings.Where(b => b.Bounds.center.x < 50).Select(b => b.Type)), "Other village changed independent allocation");
    }


    // 지원 크기의 실제 생성 지도에서 재현성과 해안 조건 검증
    private static void VerifyGeneratedMaps()
    {
        // 기존 생성 가능 시드에서 전체 지도 크기 확인
        foreach (int size in new[] { 128, 256, 512, 1024 })
        {
            var map = CityGenerator.Generate(new CitySettings { Size = size, Seed = 12345 });
            var repeat = CityGenerator.Generate(new CitySettings { Size = size, Seed = 12345, Supplies = 50 });
            Check(map.Buildings.Count == repeat.Buildings.Count, "Supplies changed building count");

            for (int i = 0; i < map.Buildings.Count; i++)
            {
                var building = map.Buildings[i];
                Check(building.Type != BuildingType.Unspecified, "Unassigned generated building");
                Check(building.Type == repeat.Buildings[i].Type && building.Bounds.Equals(repeat.Buildings[i].Bounds), "Generated seed instability");

                if (building.Type == BuildingType.CoastalOutpost)
                {
                    // 실제 지형의 모든 바다 타일에서 건물 타일까지의 최소 거리 검사
                    bool coastal = false;

                    for (int y = 0; y < size && !coastal; y++)
                    {
                        for (int x = 0; x < size && !coastal; x++)
                        {
                            byte terrain = map.Terrain[y * size + x];

                            if (terrain == CityGenerator.Ocean || terrain == CityGenerator.Shallows)
                            {
                                foreach (var tile in building.Bounds.allPositionsWithin)
                                {
                                    if ((tile - new Vector2Int(x, y)).sqrMagnitude <= 144)
                                    {
                                        coastal = true;
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    Check(coastal, "Generated inland outpost");
                }
            }

            Console.WriteLine($"PASS generated size={size}, buildings={map.Buildings.Count}, types={map.Buildings.Select(b => b.Type).Distinct().Count()}");
        }
    }


    // 검증 실패 시 해당 규칙 보고
    private static void Check(bool valid, string message)
    {
        // 성공한 비교까지 집계하고 실패 시 실행 중단
        checks++;

        if (!valid)
        {
            throw new InvalidOperationException(message);
        }
    }
}
