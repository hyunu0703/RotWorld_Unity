using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using UnityEngine;


/// <summary>
/// 변경 전후 지도와 경로 및 충돌 판정 비교
/// </summary>
public static class OptimizationVerification
{
    // 검증 결과 보관
    private static int checks;
    private static readonly List<string> results = new List<string>();

    // 편집기 일괄 비교 실행
    public static void Main()
    {
        // 실패 시 원인 기록 후 편집기 종료
        try
        {
            CompareGeneration();
            CompareQueries();
            ComparePathAllocations();
            CompareActionQueries();
            ComparePathCache();
            CompareConversionCache();
            CompareRegionalChanges();
            CompareGeometry();
            CompareNeedsSchedule();
            System.IO.File.WriteAllLines("verification.txt", results.Concat(new[] { "PASS checks=" + checks }));
            Console.WriteLine(string.Join("\n", results));
        }
        catch (Exception exception)
        {
            System.IO.File.WriteAllText("verification.txt", exception.ToString());
            Console.WriteLine(exception);
            Environment.ExitCode = 1;
        }
    }


    // 지도 생성과 경로 선택의 동일 결과 확인
    private static void CompareGeneration()
    {
        // 서로 다른 크기와 시드의 기존 생성 결과 대조
        foreach (int size in new[] { 128, 256, 512 })
        {
            foreach (int seed in new[] { 12345, 7, -129 })
            {
                Baseline.CityMap oldMap;
                try { oldMap = Baseline.CityGenerator.Generate(new Baseline.CitySettings { Size = size, Seed = seed }); }
                catch (Exception oldError) {
                    Exception newError = null;
                    try { RotWorld.CityGenerator.Generate(new RotWorld.CitySettings { Size = size, Seed = seed }); }
                    catch (Exception error) { newError = error; }
                    Check(newError != null && newError.GetType() == oldError.GetType() && newError.Message == oldError.Message, "generation failure parity");
                    results.Add("generation matched failure " + size + "/" + seed + ": " + oldError.Message);
                    continue;
                }
                var map = RotWorld.CityGenerator.Generate(new RotWorld.CitySettings { Size = size, Seed = seed });
                Check(map.Tiles.SequenceEqual(oldMap.Tiles), "tiles");
                if (size <= 256) Check(RotWorld.PixelBuilder.Build(map).SequenceEqual(Baseline.PixelBuilder.Build(oldMap)), "pixel colors");
                Check(map.Regions.SequenceEqual(oldMap.Regions.Select(x => (RotWorld.CityRegion)(byte)x)), "regions");
                Check(map.Buildings.Count == oldMap.Buildings.Count, "buildings");

                for (int i = 0; i < map.Buildings.Count; i++)
                {
                    Check(map.Buildings[i].Bounds == oldMap.Buildings[i].Bounds && map.Buildings[i].Door == oldMap.Buildings[i].Door &&
                        map.Buildings[i].Stock.SequenceEqual(oldMap.Buildings[i].Stock), "building data");
                }

                var points = new List<Vector2Int>();

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        var tile = new Vector2Int(x, y);

                        if (RotWorld.UnitPathfinder.CanOccupy(map, tile))
                        {
                            points.Add(tile);
                        }
                    }
                }

                var random = new System.Random(seed);
                var path = new List<Vector2Int>();
                var oldPath = new List<Vector2Int>();

                for (int i = 0; i < 30; i++)
                {
                    Vector2Int start = points[random.Next(points.Count)];
                    Vector2Int target = points[random.Next(points.Count)];
                    RotWorld.UnitPathfinder.FindWanderPath(map, start, new System.Random(i), path);
                    Baseline.UnitPathfinder.FindWanderPath(oldMap, start, new System.Random(i), oldPath);
                    Check(path.SequenceEqual(oldPath), "wander path");
                    RotWorld.UnitPathfinder.FindTargetPath(map, start, target, path, i % 2 == 0);
                    Baseline.UnitPathfinder.FindTargetPath(oldMap, start, target, oldPath, i % 2 == 0);
                    Check(path.SequenceEqual(oldPath), "target path");
                    var threats = new List<Vector2> { start + Vector2.right * 2f, start + Vector2.up * 3f };
                    RotWorld.UnitPathfinder.FindFleePath(map, start, start + Vector2Int.right * 2, threats, path);
                    Baseline.UnitPathfinder.FindFleePath(oldMap, start, start + Vector2Int.right * 2, threats, oldPath);
                    Check(path.SequenceEqual(oldPath), "flee path");
                }

                results.Add("generation and paths " + size + "/" + seed);
            }
        }
    }


    // 격자 검색과 기존 전체 검색의 동일 판정 확인
    private static void CompareQueries()
    {
        // 경계와 음수 및 밀집 위치를 포함한 개체 배치
        var map = new RotWorld.CityMap(128, 1);
        var oldMap = new Baseline.CityMap(128, 1);
        var random = new System.Random(491);
        var spatialType = typeof(RotWorld.CityMap).Assembly.GetType("RotWorld.UnitSpatialIndex");
        object humanIndex = Property(map, "HumanSpatialIndex");
        object zombieIndex = Property(map, "ZombieSpatialIndex");
        var setPosition = spatialType.GetMethod("SetPosition", BindingFlags.Instance | BindingFlags.NonPublic);

        for (int i = 0; i < 2000; i++)
        {
            Vector2 position = new Vector2((float)random.NextDouble() * 130f - 2f, (float)random.NextDouble() * 130f - 2f);
            map.Humans.Add(Vector2Int.RoundToInt(position));
            oldMap.Humans.Add(Vector2Int.RoundToInt(position));
            Positions(map, "HumanPositions")[i] = position;
            Positions(oldMap, "HumanPositions")[i] = position;
            setPosition.Invoke(humanIndex, new object[] { i, position });
            position += Vector2.one * .9f;
            map.Zombies.Add(Vector2Int.RoundToInt(position));
            oldMap.Zombies.Add(Vector2Int.RoundToInt(position));
            Positions(map, "ZombiePositions")[i] = position;
            Positions(oldMap, "ZombiePositions")[i] = position;
            setPosition.Invoke(zombieIndex, new object[] { i, position });
        }



        var unit = new RotWorld.UnitWander();
        var oldUnit = new Baseline.UnitWander();
        Field(unit, "map", map);
        Field(oldUnit, "map", oldMap);
        var clearance = unit.GetType().GetMethod("HasUnitClearance", BindingFlags.Instance | BindingFlags.NonPublic);
        var oldClearance = oldUnit.GetType().GetMethod("HasUnitClearance", BindingFlags.Instance | BindingFlags.NonPublic);
        var conversion = unit.GetType().GetMethod("HasConversionClearance", BindingFlags.Instance | BindingFlags.NonPublic);
        var oldConversion = oldUnit.GetType().GetMethod("HasConversionClearance", BindingFlags.Instance | BindingFlags.NonPublic);
        var points = new List<Vector2>();

        for (int i = 0; i < 3000; i++)
        {
            Vector2 from = new Vector2((float)random.NextDouble() * 128f, (float)random.NextDouble() * 128f);
            Vector2 next = from + Vector2.right * ((i % 2 == 0 ? 1f : -1f) * .1f);
            Field(unit, "position", from);
            Field(oldUnit, "position", from);
            Check((bool)clearance.Invoke(unit, new object[] { next }) == (bool)oldClearance.Invoke(oldUnit, new object[] { next }), "clearance");
            Check((bool)conversion.Invoke(unit, new object[] { next }) == (bool)oldConversion.Invoke(oldUnit, new object[] { next }), "conversion");
            Check(RotWorld.UnitSight.FindVisibleHuman(map, from) == Baseline.UnitSight.FindVisibleHuman(oldMap, from), "human sight");
            Check(RotWorld.UnitSight.FindFleeZombie(map, from, i % 2000) == Baseline.UnitSight.FindFleeZombie(oldMap, from, i % 2000), "flee sight");
            points.Add(from);
        }

        // 동일한 관찰 좌표의 감지 처리 시간 비교
        var timer = Stopwatch.StartNew();

        foreach (Vector2 point in points)
        {
            Baseline.UnitSight.FindVisibleZombie(oldMap, point);
        }

        double baselineMs = timer.Elapsed.TotalMilliseconds;
        timer.Restart();

        foreach (Vector2 point in points)
        {
            RotWorld.UnitSight.FindVisibleZombie(map, point);
        }

        double optimizedMs = timer.Elapsed.TotalMilliseconds;

        for (int i = 0; i < 2000; i++)
        {
            int index = i % 2000;
            Vector2 moved = new Vector2(i % 128 + .0001f, (i * 13) % 128 - .0001f);
            map.HumanPositions[index] = moved;
            oldMap.HumanPositions[index] = moved;
            map.HumanSpatialIndex.SetPosition(index, moved);

            if (i % 7 == 0)
            {
                map.ZombiePositions.Remove(index);
                oldMap.ZombiePositions.Remove(index);
                map.ZombieSpatialIndex.Remove(index);
                map.InactiveZombies.Add(index);
                oldMap.InactiveZombies.Add(index);
            }
            else
            {
                map.ZombiePositions[index] = moved;
                oldMap.ZombiePositions[index] = moved;
                map.ZombieSpatialIndex.SetPosition(index, moved);
            }

            Check(RotWorld.UnitSight.FindVisibleHuman(map, moved) == Baseline.UnitSight.FindVisibleHuman(oldMap, moved), "moved sight");
            Check(RotWorld.UnitSight.FindVisibleZombie(map, moved) == Baseline.UnitSight.FindVisibleZombie(oldMap, moved), "removed sight");
            Check((bool)clearance.Invoke(unit, new object[] { moved }) == (bool)oldClearance.Invoke(oldUnit, new object[] { moved }), "moved clearance");
        }

        results.Add("sight benchmark baseline_ms=" + baselineMs + " optimized_ms=" + optimizedMs);


    }


    // 예열 후 동일 경로 요청의 할당량과 결과 확인
    private static void ComparePathAllocations()
    {
        // 장애물과 점유가 없는 고정 지도의 반복 탐색 준비
        var map = new RotWorld.CityMap(32, 1);
        var oldMap = new Baseline.CityMap(32, 1);
        var path = new List<Vector2Int>();
        var oldPath = new List<Vector2Int>();
        Vector2Int start = new Vector2Int(1, 1);
        Vector2Int target = new Vector2Int(25, 25);
        RotWorld.UnitPathfinder.FindTargetPath(map, start, target, path);
        Baseline.UnitPathfinder.FindTargetPath(oldMap, start, target, oldPath);
        long allocated = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 100; i++)
        {
            Baseline.UnitPathfinder.FindTargetPath(oldMap, start, target, oldPath);
        }

        long baselineBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        allocated = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 100; i++)
        {
            RotWorld.UnitPathfinder.FindTargetPath(map, start, target, path);
        }

        long optimizedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Check(path.SequenceEqual(oldPath), "allocation benchmark path");
        Check(optimizedBytes < baselineBytes, "allocation reduction");
        results.Add("100 target searches allocated baseline=" + baselineBytes + " optimized=" + optimizedBytes);
    }


    // 실제 이동 좌표와 사전 키 재사용 이후 반격 및 위협 결과 비교
    private static void CompareActionQueries()
    {
        // 공유 좌표 갱신 전의 이동도 행동 검색에 즉시 반영
        var map = new RotWorld.CityMap(128, 1);
        var oldMap = new Baseline.CityMap(128, 1);
        var random = new System.Random(917);

        for (int i = 0; i < 300; i++)
        {
            var unit = new RotWorld.UnitWander();
            var oldUnit = new Baseline.UnitWander();
            Field(unit, "map", map);
            Field(oldUnit, "map", oldMap);
            Field(unit, "isZombie", true);
            Field(oldUnit, "isZombie", true);
            Field(unit, "populationIndex", i);
            Field(oldUnit, "populationIndex", i);
            map.RegisterUnit(true, i, unit);
            oldMap.ZombieUnits.Add(i, oldUnit);
            Vector2 position = new Vector2((float)random.NextDouble() * 20, (float)random.NextDouble() * 20);
            Invoke(unit, "SetPosition", position);
            Invoke(oldUnit, "SetPosition", position);
            Invoke(unit, "SetChaseTarget", i % 5);
            Invoke(oldUnit, "SetChaseTarget", i % 5);
        }

        var human = new RotWorld.UnitWander();
        var oldHuman = new Baseline.UnitWander();
        Field(human, "map", map);
        Field(oldHuman, "map", oldMap);
        Field(human, "populationIndex", 3);
        Field(oldHuman, "populationIndex", 3);

        for (int i = 0; i < 2000; i++)
        {
            // 제거 후 새 키 삽입으로 숫자 인덱스와 사전 순회 순서 분리
            if (i % 17 == 0)
            {
                int removed = i / 17;
                map.RemoveUnit(true, removed);
                oldMap.ZombieUnits.Remove(removed);
                var unit = new RotWorld.UnitWander();
                var oldUnit = new Baseline.UnitWander();
                int index = 300 + removed;
                Field(unit, "map", map);
                Field(oldUnit, "map", oldMap);
                Field(unit, "isZombie", true);
                Field(oldUnit, "isZombie", true);
                Field(unit, "populationIndex", index);
                Field(oldUnit, "populationIndex", index);
                map.RegisterUnit(true, index, unit);
                oldMap.ZombieUnits.Add(index, oldUnit);
                Invoke(unit, "SetPosition", new Vector2(10f, 10f));
                Invoke(oldUnit, "SetPosition", new Vector2(10f, 10f));
            }

            int moving = 200 + i % 100;
            Vector2 point = new Vector2((float)random.NextDouble() * 20, (float)random.NextDouble() * 20);
            Invoke(map.ZombieUnits[moving], "SetPosition", point);
            Invoke(oldMap.ZombieUnits[moving], "SetPosition", point);
            Invoke(map.ZombieUnits[moving], "SetChaseTarget", i % 7 - 1);
            Invoke(oldMap.ZombieUnits[moving], "SetChaseTarget", i % 7 - 1);
            Field(human, "position", point);
            Field(oldHuman, "position", point);
            var selected = RotWorld.CombatSelection.Find(map, point);
            var previous = Baseline.CombatSelection.Find(oldMap, point);
            Check((selected == null ? -1 : selected.PopulationIndex) == (previous == null ? -1 : previous.PopulationIndex), "attack target order");
            Check((bool)Invoke(human, "HasOtherZombieThreat", map.ZombieUnits[moving]) ==
                (bool)Invoke(oldHuman, "HasOtherZombieThreat", oldMap.ZombieUnits[moving]), "cooperative threat");
        }

        // 같은 거리에서 사전 슬롯 재사용 이후의 대상 선택 순서 확인
        foreach (var pair in map.ZombieUnits)
        {
            Invoke(pair.Value, "SetPosition", Vector2.one * 10);
            Invoke(oldMap.ZombieUnits[pair.Key], "SetPosition", Vector2.one * 10);
        }

        Check(RotWorld.CombatSelection.Find(map, Vector2.one * 10).PopulationIndex ==
            Baseline.CombatSelection.Find(oldMap, Vector2.one * 10).PopulationIndex, "equal distance dictionary order");
        // 한 좀비에 대한 지원자 모집의 거리 및 행동 가능 조건 비교
        var helperMap = new RotWorld.CityMap(32, 1);
        var oldHelperMap = new Baseline.CityMap(32, 1);
        var target = new RotWorld.UnitWander();
        var oldTarget = new Baseline.UnitWander();
        Field(target, "map", helperMap);
        Field(oldTarget, "map", oldHelperMap);
        Field(target, "isZombie", true);
        Field(oldTarget, "isZombie", true);
        helperMap.RegisterUnit(true, 0, target);
        oldHelperMap.ZombieUnits.Add(0, oldTarget);
        Invoke(target, "SetChaseTarget", 0);
        Invoke(oldTarget, "SetChaseTarget", 0);

        for (int i = 0; i < 50; i++)
        {
            var candidate = new RotWorld.UnitWander();
            var oldCandidate = new Baseline.UnitWander();
            Field(candidate, "map", helperMap);
            Field(oldCandidate, "map", oldHelperMap);
            Field(candidate, "populationIndex", i);
            Field(oldCandidate, "populationIndex", i);
            helperMap.RegisterUnit(false, i, candidate);
            oldHelperMap.HumanUnits.Add(i, oldCandidate);
            Invoke(candidate, "SetPosition", Vector2.one * 10);
            Invoke(oldCandidate, "SetPosition", Vector2.one * 10);
        }

        for (int i = 0; i < 500; i++)
        {
            int index = 1 + i % 49;
            Vector2 point = new Vector2((float)random.NextDouble() * 20, (float)random.NextDouble() * 20);
            Invoke(helperMap.HumanUnits[index], "SetPosition", point);
            Invoke(oldHelperMap.HumanUnits[index], "SetPosition", point);
            helperMap.HumanUnits[index].isActiveAndEnabled = i % 3 != 0;
            oldHelperMap.HumanUnits[index].isActiveAndEnabled = i % 3 != 0;
            var candidate = (RotWorld.UnitWander)Invoke(helperMap.HumanUnits[0], "FindAvailableCooperativeHelper", target);
            var oldCandidate = (Baseline.UnitWander)Invoke(oldHelperMap.HumanUnits[0], "FindAvailableCooperativeHelper", oldTarget);
            Check((candidate == null ? -1 : candidate.PopulationIndex) == (oldCandidate == null ? -1 : oldCandidate.PopulationIndex), "cooperative helper selection");
        }

        // 통지 대상이 적거나 많은 경우 모두 원래 사전 순서 유지
        var sparse = new List<int> { 43, 7 };
        helperMap.HumanQueries.SortInOriginalOrder(sparse);
        Check(sparse.SequenceEqual(helperMap.HumanUnits.Keys.Where(index => index == 43 || index == 7)), "sparse dependency order");
        var dense = helperMap.HumanUnits.Keys.Where(index => index % 2 == 0).Reverse().ToList();
        helperMap.HumanQueries.SortInOriginalOrder(dense);
        Check(dense.SequenceEqual(helperMap.HumanUnits.Keys.Where(index => index % 2 == 0)), "dense dependency order");

        results.Add("action queries and cooperative threat parity passed");
    }


    // 점유와 출발 및 목표 변경에 따른 경로 캐시 무효화 확인
    private static void ComparePathCache()
    {
        // 지형 생성이 끝난 고정 지도와 재사용 경로 준비
        var map = new RotWorld.CityMap(32, 1);
        var cache = new RotWorld.UnitPathfinder.TargetPathCache();
        var path = new List<Vector2Int>();
        var expected = new List<Vector2Int>();
        Vector2Int start = new Vector2Int(2, 2);
        Vector2Int target = new Vector2Int(20, 20);

        for (int i = 0; i < 100; i++)
        {
            Vector2Int occupied = new Vector2Int(3 + i % 10, 3 + i % 10);
            map.OccupyTile(occupied);
            cache.Find(map, start, target, path);
            RotWorld.UnitPathfinder.FindTargetPath(map, start, target, expected);
            Check(path.SequenceEqual(expected), "occupied cache invalidation");
            cache.Find(map, start, target, path);
            Check(path.SequenceEqual(expected), "same state cache");
            map.ReleaseTile(occupied);
            cache.Find(map, start, target, path);
            RotWorld.UnitPathfinder.FindTargetPath(map, start, target, expected);
            Check(path.SequenceEqual(expected), "released cache invalidation");
            start = new Vector2Int(2 + i % 2, 2);
            target = new Vector2Int(20, 20 + i % 2);
        }

        results.Add("path cache exact input invalidation passed");
    }


    // 행동 변경 메서드의 독립 검증 호출
    private static object Invoke(object target, string method, object argument)
    {
        // Unity 콜백을 제외한 실제 비공개 계산 실행
        return target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, new[] { argument });
    }


    // 공간이 막힌 변환 검색의 재사용과 위치 변경 직후 재시도 확인
    private static void CompareConversionCache()
    {
        // 주변 후보 전체를 실제 충돌 좌표로 점유
        var map = new RotWorld.CityMap(32, 1);
        var oldMap = new Baseline.CityMap(32, 1);
        var human = new RotWorld.UnitWander();
        var oldHuman = new Baseline.UnitWander();
        Field(human, "map", map);
        Field(oldHuman, "map", oldMap);
        Field(human, "populationIndex", 999);
        Field(oldHuman, "populationIndex", 999);
        Field(human, "position", Vector2.one * 16);
        Field(oldHuman, "position", Vector2.one * 16);
        int index = 0;

        for (int y = 12; y <= 20; y++)
        {
            for (int x = 12; x <= 20; x++)
            {
                Vector2 point = new Vector2(x, y);
                map.HumanPositions[index] = point;
                oldMap.HumanPositions[index] = point;
                map.HumanSpatialIndex.SetPosition(index, point);
                index++;
            }
        }

        Check(human.TryConversion() == oldHuman.TryConversion(), "blocked conversion parity");
        int searches = human.TerrainChecks;
        Check(human.TryConversion() == oldHuman.TryConversion() && searches == human.TerrainChecks, "unchanged failed conversion cache");
        map.HumanPositions.Remove(40);
        oldMap.HumanPositions.Remove(40);
        map.HumanSpatialIndex.Remove(40);
        map.NotifyPositionChanged();
        Check(human.TryConversion() == oldHuman.TryConversion() && human.TerrainChecks > searches, "conversion immediate retry after position change");
        results.Add("conversion cache failure and immediate retry parity passed");
    }


    // 무관한 영역과 실제 점유 의존 영역의 캐시 무효화 구분
    private static void CompareRegionalChanges()
    {
        // 가까운 목표의 성공과 막힌 출발점의 실패 경로 준비
        var map = new RotWorld.CityMap(64, 1);
        var cache = new RotWorld.UnitPathfinder.TargetPathCache();
        var path = new List<Vector2Int>();
        var expected = new List<Vector2Int>();
        Vector2Int start = new Vector2Int(2, 2);
        Vector2Int target = new Vector2Int(6, 6);
        cache.Find(map, start, target, path);
        var watch = (RotWorld.GridChangeTracker.Watch)cache.GetType().GetField("occupancyWatch", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(cache);
        map.OccupyTile(new Vector2Int(60, 60));
        Check(watch.TryRefresh(map.OccupancyChanges), "far occupancy keeps cache");
        map.OccupyTile(new Vector2Int(3, 3));
        Check(!watch.TryRefresh(map.OccupancyChanges), "consulted occupancy invalidates cache");
        var random = new System.Random(201);

        for (int i = 0; i < 1000; i++)
        {
            Vector2Int tile = new Vector2Int(random.Next(1, 64), random.Next(1, 64));

            if (i % 2 == 0)
            {
                map.OccupyTile(tile);
            }
            else
            {
                map.ReleaseTile(tile);
            }

            cache.Find(map, start, target, path);
            RotWorld.UnitPathfinder.FindTargetPath(map, start, target, expected);
            Check(path.SequenceEqual(expected), "regional cache dynamic parity");
        }

        var positions = new RotWorld.GridChangeTracker.Watch();
        positions.Reset(map.PositionChanges);
        positions.RecordArea(Vector2.one * 16, 4.901f);
        map.PublishUnitPosition(false, 0, Vector2.one * 60);
        Check(positions.TryRefresh(map.PositionChanges), "far position keeps conversion cache");
        map.PublishUnitPosition(false, 0, Vector2.one * 12);
        Check(!positions.TryRefresh(map.PositionChanges), "entry to conversion area invalidates cache");
        results.Add("regional cache distant and relevant change checks passed");
    }


    // 시야와 몸체 검사의 모서리 및 긴 구간 결과 비교
    private static void CompareGeometry()
    {
        // 해안과 건물 및 빈 지형을 섞은 동일 지도 구성
        var map = new RotWorld.CityMap(64, 1);
        var oldMap = new Baseline.CityMap(64, 1);
        var random = new System.Random(617);

        for (int i = 0; i < map.Tiles.Length; i++)
        {
            byte tile = random.Next(60) == 0 ? (byte)3 : random.Next(60) == 0 ? (byte)5 : (byte)0;
            map.Tiles[i] = oldMap.Tiles[i] = tile;
        }

        var unit = new RotWorld.UnitWander();
        var oldUnit = new Baseline.UnitWander();
        Field(unit, "map", map);
        Field(oldUnit, "map", oldMap);
        var terrain = unit.GetType().GetMethod("IsTerrainClear", BindingFlags.NonPublic | BindingFlags.Instance);
        var oldTerrain = oldUnit.GetType().GetMethod("IsTerrainClear", BindingFlags.NonPublic | BindingFlags.Instance);

        for (int i = 0; i < 10000; i++)
        {
            Vector2 from = new Vector2((float)random.NextDouble() * 68 - 2, (float)random.NextDouble() * 68 - 2);
            Vector2 to = new Vector2((float)random.NextDouble() * 68 - 2, (float)random.NextDouble() * 68 - 2);

            if (i % 3 == 0)
            {
                from = Vector2Int.RoundToInt(from);
                to = i % 2 == 0 ? new Vector2(to.x, from.y) : new Vector2(from.x, to.y);
            }

            Check(RotWorld.UnitSight.HasLineOfSight(map, from, to) == Baseline.UnitSight.HasLineOfSight(oldMap, from, to), "row clipped sight parity");
            Check((bool)terrain.Invoke(unit, new object[] { from, to }) == (bool)oldTerrain.Invoke(oldUnit, new object[] { from, to }), "sampled terrain parity");
        }

        results.Add("10000 line-of-sight and terrain segment comparisons passed");
    }


    // 생성 시점 공유와 개인 분리 및 중단의 수치 비트 비교
    private static void CompareNeedsSchedule()
    {
        // 서로 다른 생성 시점과 일시정지 및 고갈 경계 구성
        var schedule = new RotWorld.HumanNeeds.Schedule();
        var current = new List<RotWorld.HumanNeeds>();
        var previous = new List<Baseline.HumanNeeds>();
        var active = new List<bool>();
        var random = new System.Random(427);

        for (int frame = 0; frame < 1000; frame++)
        {
            if (frame < 20)
            {
                for (int birth = 0; birth < 20; birth++)
                {
                    current.Add(schedule.Create());
                    previous.Add(new Baseline.HumanNeeds());
                    active.Add(true);
                }
            }

            if (frame % 17 == 0)
            {
                int index = random.Next(current.Count);
                current[index].Advance(.123456789);
                previous[index].Advance(.123456789);
            }

            if (frame % 23 == 0)
            {
                int index = random.Next(current.Count);
                current[index].Freeze();
                active[index] = false;
            }

            double seconds = frame == 900 ? 700000d : frame % 19 == 0 ? 0d : random.NextDouble() * 10;
            schedule.Advance(seconds);

            for (int i = 0; i < current.Count; i++)
            {
                if (active[i])
                {
                    previous[i].Advance(seconds);
                }

                Check(BitConverter.DoubleToInt64Bits(current[i].Hunger) == BitConverter.DoubleToInt64Bits(previous[i].Hunger), "shared hunger exact arithmetic");
                Check(BitConverter.DoubleToInt64Bits(current[i].Thirst) == BitConverter.DoubleToInt64Bits(previous[i].Thirst), "shared thirst exact arithmetic");
            }
        }

        results.Add("shared needs creation, split, freeze and depletion exact arithmetic passed");
    }


    // 비공개 속성의 검증용 조회
    private static object Property(object target, string name)
    {
        // 실제 타입의 내부 자료구조 참조 반환
        return target.GetType().GetProperty(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);
    }


    // 좌표 사전의 검증용 조회
    private static Dictionary<int, Vector2> Positions(object target, string name)
    {
        // 기존 구현과 동일한 좌표 저장소 반환
        return (Dictionary<int, Vector2>)Property(target, name);
    }


    // 검증 개체의 입력 상태 지정
    private static void Field(object target, string name, object value)
    {
        // 동작 메서드 실행 전 대응하는 기존 필드 설정
        target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
    }


    // 결과 불일치 시 검증 중단
    private static void Check(bool valid, string label)
    {
        // 최초 불일치의 검사 이름 전달
        checks++;

        if (!valid)
        {
            throw new Exception("Mismatch " + label + " at " + checks);
        }
    }
}
