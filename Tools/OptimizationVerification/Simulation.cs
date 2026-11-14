using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

/// <summary>
/// 실제 이동과 전투 코드의 연속 상태 비교
/// </summary>
public static class SimulationVerification
{
    // 연속 실행의 비교 개수와 동작 관찰 기록
    private static int checks;
    private static int cooperativeFrames;
    private static int attackFrames;
    private static int converted;

    // 결정적 프레임 입력으로 이전 코드와 현재 코드 비교
    public static void Main()
    {
        // 실제 Unity 콜백 대신 같은 순서로 Update 호출
        Run(16, 1, 1800, false);
        Run(15, 45, 1000, true);
        Run(40, 80, 700, true);
        Console.WriteLine($"PASS simulation comparisons={checks}, cooperative observations={cooperativeFrames}, attacks={attackFrames}, converted={converted}");
    }


    // 고정 지도에서 대량 생성 후 양쪽의 프레임별 상태 확인
    private static void Run(int humans, int zombies, int frames, bool wall)
    {
        // 동일한 지형과 초기 개체 순서 구성
        var map = new RotWorld.CityMap(32, 12345);
        var oldMap = new Baseline.CityMap(32, 12345);
        var points = new List<Vector2Int>();

        for (int y = 0; y < 32; y++)
        {
            for (int x = 0; x < 32; x++)
            {
                byte tile = wall && x == 16 && y > 3 && y < 27 && y != 15 ? (byte)3 : (byte)0;
                map.Tiles[y * 32 + x] = tile;
                oldMap.Tiles[y * 32 + x] = tile;

                if (tile == 0 && x >= 10 && x < 26 && y >= 10 && y < 26)
                {
                    points.Add(new Vector2Int(x, y));
                }
            }
        }

        var units = new List<RotWorld.UnitWander>();
        var oldUnits = new List<Baseline.UnitWander>();
        var sprites = new[] { new Sprite(), new Sprite(), new Sprite() };

        for (int i = 0; i < humans + zombies; i++)
        {
            bool zombie = i >= humans;
            int index = zombie ? i - humans : i;
            Vector2Int tile = points[i];
            (zombie ? map.Zombies : map.Humans).Add(tile);
            (zombie ? oldMap.Zombies : oldMap.Humans).Add(tile);
            var actor = new GameObject("current");
            actor.transform.localPosition = new Vector3(tile.x, tile.y, 0);
            actor.AddComponent<RotWorld.UnitWalkAnimation>();
            var unit = actor.AddComponent<RotWorld.UnitWander>();
            unit.Initialize(map, zombie, index, sprites, sprites);
            units.Add(unit);
            var previous = new GameObject("baseline");
            previous.transform.localPosition = actor.transform.localPosition;
            previous.AddComponent<Baseline.UnitWalkAnimation>();
            var oldUnit = previous.AddComponent<Baseline.UnitWander>();
            oldUnit.Initialize(oldMap, zombie, index, sprites, sprites);
            oldUnits.Add(oldUnit);
        }

        var clock = new RotWorld.GameClock();

        for (int frame = 0; frame < frames; frame++)
        {
            // 정지와 배속 및 일반 시간의 동일 입력
            Time.frameCount = frame;
            Time.deltaTime = frame % 97 == 0 ? 0f : frame % 151 == 0 ? .13333333f : 1f / 60f;
            Time.time += Time.deltaTime;
            double previousSeconds = clock.TotalElapsedSeconds;
            clock.Advance(Time.deltaTime);
            double seconds = clock.TotalElapsedSeconds - previousSeconds;
            map.NeedsSchedule.Advance(seconds);

            foreach (var oldHuman in oldMap.HumanUnits.Values)
            {
                if (oldHuman.IsHumanTarget)
                {
                    oldHuman.Needs.Advance(seconds);
                }
            }

            for (int i = 0; i < units.Count; i++)
            {
                if (units[i].gameObject.destroyed)
                {
                    continue;
                }

                Update(units[i]);
                Update(oldUnits[i]);
                Update(units[i].gameObject.GetComponent<RotWorld.UnitCombat>());
                Update(oldUnits[i].gameObject.GetComponent<Baseline.UnitCombat>());
            }

            // 일부 개체의 쓰러짐과 감염 및 관계 정리까지 재현
            if (frame == 240 || frame == 480)
            {
                int index = frame == 240 ? 0 : humans;

                for (int hit = 0; hit < 5; hit++)
                {
                    units[index].ReceiveHit(Vector2.right);
                    oldUnits[index].ReceiveHit(Vector2.right);
                }
            }

            // 진행 중 추가 소환으로 새 시간 묶음과 사전 슬롯 재사용 검증
            if (frame == 300)
            {
                int summoned = 0;

                foreach (Vector2Int tile in points)
                {
                    if (map.OccupiedTiles.Contains(tile) || summoned >= 30)
                    {
                        continue;
                    }

                    bool zombie = summoned % 3 == 0;
                    int index = zombie ? map.Zombies.Count : map.Humans.Count;
                    (zombie ? map.Zombies : map.Humans).Add(tile);
                    (zombie ? oldMap.Zombies : oldMap.Humans).Add(tile);
                    var actor = new GameObject("current summoned");
                    actor.transform.localPosition = new Vector3(tile.x, tile.y, 0f);
                    actor.AddComponent<RotWorld.UnitWalkAnimation>();
                    var unit = actor.AddComponent<RotWorld.UnitWander>();
                    unit.Initialize(map, zombie, index, sprites, sprites);
                    units.Add(unit);
                    var previous = new GameObject("baseline summoned");
                    previous.transform.localPosition = actor.transform.localPosition;
                    previous.AddComponent<Baseline.UnitWalkAnimation>();
                    var oldUnit = previous.AddComponent<Baseline.UnitWander>();
                    oldUnit.Initialize(oldMap, zombie, index, sprites, sprites);
                    oldUnits.Add(oldUnit);
                    summoned++;
                }
            }

            Object.Flush();

            for (int i = 0; i < units.Count; i++)
            {
                var unit = units[i];
                var old = oldUnits[i];
                string label = $"{humans}/{zombies} frame={frame} unit={i}";
                Check(unit.gameObject.destroyed == old.gameObject.destroyed, "destroy " + label);

                if (unit.gameObject.destroyed)
                {
                    continue;
                }

                Check(unit.Position.Equals(old.Position), "position " + label);
                Check(unit.IsHumanTarget == old.IsHumanTarget && unit.IsZombieTarget == old.IsZombieTarget, "type " + label);

                foreach (string field in new[] { "chaseTarget", "isDowned", "hitCount", "pathIndex", "knockbackElapsed", "riseRemaining" })
                {
                    Check(F(unit, field).Equals(F(old, field)), field + " " + label);
                }

                foreach (string field in new[] { "cooperativeLeader", "cooperativeHelper", "cooperativeSecondHelper", "cooperativeZombie", "cooperativeRescueTarget", "cooperativeRescuer", "cooperativeNextAttacker" })
                {
                    Check(Id(F(unit, field)) == Id(F(old, field)), field + " " + label);
                }

                Check(((List<Vector2Int>)F(unit, "path")).SequenceEqual((List<Vector2Int>)F(old, "path")), "path " + label);

                if (unit.Needs != null)
                {
                    Check(BitConverter.DoubleToInt64Bits(unit.Needs.Hunger) == BitConverter.DoubleToInt64Bits(old.Needs.Hunger), "hunger " + label);
                    Check(BitConverter.DoubleToInt64Bits(unit.Needs.Thirst) == BitConverter.DoubleToInt64Bits(old.Needs.Thirst), "thirst " + label);
                }

                var combat = unit.gameObject.GetComponent<RotWorld.UnitCombat>();
                var oldCombat = old.gameObject.GetComponent<Baseline.UnitCombat>();

                foreach (string field in new[] { "cooldown", "attackTime", "flashRemaining", "pursuitDelayRemaining" })
                {
                    Check(F(combat, field).Equals(F(oldCombat, field)), field + " " + label);
                }

                Check(F(combat, "isAttacking").Equals(F(oldCombat, "isAttacking")) && Id(F(combat, "victim")) == Id(F(oldCombat, "victim")), "attack " + label);
                cooperativeFrames += F(unit, "cooperativeZombie") == null ? 0 : 1;
                attackFrames += (bool)F(combat, "isAttacking") ? 1 : 0;
            }
        }

        converted += map.ConvertedHumans.Count;
        Console.WriteLine($"scenario {humans}+{zombies}, {frames} frames passed, converted={map.ConvertedHumans.Count}, zombie deaths={map.InactiveZombies.Count}");

        foreach (var unit in units)
        {
            Object.Destroy(unit.gameObject);
        }

        foreach (var unit in oldUnits)
        {
            Object.Destroy(unit.gameObject);
        }

        Object.Flush();
    }


    // 비교 대상 개체의 논리 식별자 조회
    private static string Id(object unit)
    {
        // 타입 이름 대신 인간 및 좀비 인덱스 조합 사용
        return unit == null ? "null" : F(unit, "isZombie") + ":" + F(unit, "populationIndex");
    }


    // 실제 코드의 내부 상태 조회
    private static object F(object unit, string field)
    {
        // 같은 역할의 필드를 이전 타입과 현재 타입에서 각각 확인
        return unit.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(unit);
    }


    // 활성 컴포넌트의 실제 프레임 처리 호출
    private static void Update(MonoBehaviour unit)
    {
        // Unity 스케줄러와 독립적인 결정적 실행 순서
        if (unit.enabled)
        {
            unit.GetType().GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(unit, null);
        }
    }


    // 최초 상태 불일치에서 검증 중단
    private static void Check(bool valid, string label)
    {
        // 실패 프레임과 개체를 예외 메시지에 포함
        checks++;

        if (!valid)
        {
            throw new Exception(label);
        }
    }
}
