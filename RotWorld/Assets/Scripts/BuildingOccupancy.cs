using System;
using System.Collections.Generic;
using UnityEngine;

namespace RotWorld
{
    /// <summary>
    /// 건물 내부 인간 집합과 인원 수 및 주변 출입구 조회 제공
    /// </summary>
    public sealed class BuildingOccupancy
    {
        // 건물별 내부 인간과 인원이 있는 건물 목록
        private readonly Dictionary<CityBuilding, HashSet<UnitWander>> occupants = new Dictionary<CityBuilding, HashSet<UnitWander>>();
        private readonly List<CityBuilding> occupiedBuildings = new List<CityBuilding>();
        private readonly Stack<HashSet<UnitWander>> availableSets = new Stack<HashSet<UnitWander>>();
        public IReadOnlyList<CityBuilding> OccupiedBuildings => occupiedBuildings;

        // 인원 변경을 UI에 알리는 변경 번호
        public int Version { get; private set; }

        // 출입구 주변 검색 격자
        private const int DoorCellSize = 8;
        private readonly Dictionary<Vector2Int, List<CityBuilding>> doorCells = new Dictionary<Vector2Int, List<CityBuilding>>();
        private bool isDoorIndexReady;
        private readonly CityMap map;


        // 소유 지도 연결
        internal BuildingOccupancy(CityMap map)
        {
            // 건물 목록은 생성 후 고정되므로 첫 출입구 조회 시 격자 구성
            this.map = map ?? throw new ArgumentNullException(nameof(map));
        }


        // 건물 내부 인원 수 조회
        public int GetCount(CityBuilding building)
        {
            // 등록되지 않은 건물은 빈 건물로 취급
            return building != null && occupants.TryGetValue(building, out HashSet<UnitWander> members) ? members.Count : 0;
        }


        // 인간의 건물 진입 기록
        internal void Enter(CityBuilding building, UnitWander human)
        {
            // 필수 대상 확인 후 중복 진입은 인원 변경 없이 무시
            if (building == null || human == null)
            {
                throw new ArgumentNullException(building == null ? nameof(building) : nameof(human));
            }

            if (!occupants.TryGetValue(building, out HashSet<UnitWander> members))
            {
                members = availableSets.Count > 0 ? availableSets.Pop() : new HashSet<UnitWander>();
                occupants.Add(building, members);
                occupiedBuildings.Add(building);
            }

            if (members.Add(human))
            {
                Version++;
            }
        }


        // 인간의 건물 퇴장 또는 사망 기록
        internal void Exit(CityBuilding building, UnitWander human)
        {
            // 기록이 없는 퇴장 요청은 변경 없이 종료
            if (building == null || !occupants.TryGetValue(building, out HashSet<UnitWander> members) || !members.Remove(human))
            {
                return;
            }

            // 마지막 인원이 나간 건물은 표시 목록에서 제거하고 집합 재사용
            if (members.Count == 0)
            {
                occupants.Remove(building);
                occupiedBuildings.Remove(building);
                availableSets.Push(members);
            }

            Version++;
        }


        // 지정 반경 안에 출입구가 있는 건물 수집
        internal void CollectBuildingsNear(Vector2 position, float radius, List<CityBuilding> results)
        {
            // 최초 요청 시 고정 건물 목록으로 출입구 격자 구성
            results.Clear();
            EnsureDoorIndex();

            Vector2Int min = GetDoorCell(position - Vector2.one * radius);
            Vector2Int max = GetDoorCell(position + Vector2.one * radius);
            float radiusSquared = radius * radius;

            for (int y = min.y; y <= max.y; y++)
            {
                for (int x = min.x; x <= max.x; x++)
                {
                    if (!doorCells.TryGetValue(new Vector2Int(x, y), out List<CityBuilding> buildings))
                    {
                        continue;
                    }

                    foreach (var building in buildings)
                    {
                        if (((Vector2)building.Door - position).sqrMagnitude <= radiusSquared)
                        {
                            results.Add(building);
                        }
                    }
                }
            }
        }


        // 건물 출입구 좌표를 검색 격자에 등록
        private void EnsureDoorIndex()
        {
            // 이미 구성한 격자 재사용
            if (isDoorIndexReady)
            {
                return;
            }

            foreach (var building in map.Buildings)
            {
                Vector2Int cell = GetDoorCell(building.Door);

                if (!doorCells.TryGetValue(cell, out List<CityBuilding> buildings))
                {
                    buildings = new List<CityBuilding>();
                    doorCells.Add(cell, buildings);
                }

                buildings.Add(building);
            }

            isDoorIndexReady = true;
        }


        // 지도 좌표의 출입구 격자 변환
        private static Vector2Int GetDoorCell(Vector2 position)
        {
            // 경계 양쪽 출입구의 누락을 막는 내림 변환
            return new Vector2Int(Mathf.FloorToInt(position.x / DoorCellSize), Mathf.FloorToInt(position.y / DoorCellSize));
        }
    }
}
