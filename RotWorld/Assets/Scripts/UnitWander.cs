using System.Collections.Generic;
using UnityEngine;

namespace RotWorld
{
    /// <summary>
    /// 주변 배회와 시야 내 인간 추적 및 경로 이동 제어
    /// </summary>
    public sealed class UnitWander : MonoBehaviour
    {
        // 유형별 기본 능력치와 개체 속도 편차
        [SerializeField] private float humanSpeed = 1.8f;
        [SerializeField] private float zombieSpeed = 1.2f;
        private const float MinimumSpeedVariation = .85f;
        private const float MaximumSpeedVariation = 1.15f;
        private const float MinimumRest = 1f;
        private const float MaximumRest = 6f;

        // 지도 인구와 연속 지면 위치 및 이동 경로 보관
        private CityMap map;
        private List<Vector2Int> population;
        private int populationIndex;
        private readonly List<Vector2Int> path = new List<Vector2Int>();
        private int pathIndex;
        private Vector2 position;
        private Vector2Int currentTile;

        // 개체별 난수와 이동 속도 및 걷기 표시 연결
        private System.Random random;
        private float speed;
        private float restRemaining;
        private UnitWalkAnimation walkAnimation;

        // 좀비 감지 주기와 대상 변경에 따른 경로 재계산 상태
        private const float DetectionInterval = .2f;
        private bool isZombie;
        private float detectionRemaining;
        private int chaseTarget = -1;
        private bool hasChasePath;
        private Vector2Int chaseDestination;

        // 기존 인구 데이터와 이동 및 걷기 표시 연결
        public void Initialize(CityMap map, bool zombie, int index)
        {
            // 필수 지도와 표시 컴포넌트 및 인구 인덱스 검증
            var units = map == null ? null : zombie ? map.Zombies : map.Humans;

            if (units == null || index < 0 || index >= units.Count || !TryGetComponent(out walkAnimation))
            {
                Debug.LogError("유닛 이동 초기화에 필요한 지도, 인구 또는 걷기 표시 누락", this);
                enabled = false;
                return;
            }

            this.map = map;
            isZombie = zombie;
            population = units;
            populationIndex = index;
            currentTile = units[index];
            position = currentTile;
            random = new System.Random(unchecked(map.Seed * 397 ^ index * 7919 ^ (zombie ? 1 : 0)));
            speed = (zombie ? zombieSpeed : humanSpeed) * Mathf.Lerp(MinimumSpeedVariation, MaximumSpeedVariation, (float)random.NextDouble());
            Rest();
        }


        // 경로를 연속 이동하고 목적지 도착 후에만 휴식
        private void Update()
        {
            // 초기화 전과 시간 정지 중 처리 생략
            if (map == null || Time.deltaTime <= 0f)
            {
                return;
            }

            UpdateChase();

            if (pathIndex >= path.Count)
            {
                restRemaining -= Time.deltaTime;

                if (restRemaining <= 0f && chaseTarget < 0)
                {
                    UnitPathfinder.FindWanderPath(map, currentTile, random, path);
                    pathIndex = 0;

                    if (path.Count == 0)
                    {
                        Rest();
                    }
                }

                return;
            }

            // 프레임 잔여 시간을 다음 구간에 넘겨 타일 경계의 멈춤 방지
            float remainingTime = Time.deltaTime;
            float travelled = 0f;
            Vector2 direction = Vector2.zero;

            while (remainingTime > 0f && pathIndex < path.Count)
            {
                Vector2Int target = path[pathIndex];
                direction = (Vector2)target - position;
                float distance = direction.magnitude;
                float terrainSpeed = speed * UnitPathfinder.SpeedMultiplier(map, currentTile);
                float step = Mathf.Min(distance, remainingTime * terrainSpeed);
                position = Vector2.MoveTowards(position, target, step);
                travelled += step;
                remainingTime -= step / terrainSpeed;

                if (step < distance)
                {
                    break;
                }

                currentTile = target;

                pathIndex++;

                if (isZombie)
                {
                    UpdateChase(false);
                }
            }

            // 논리 좌표와 표시 위치 동기화 및 실제 이동 거리 기반 걷기 모션 갱신
            population[populationIndex] = new Vector2Int(Mathf.RoundToInt(position.x), Mathf.RoundToInt(position.y));
            transform.localPosition = new Vector3(position.x, position.y, 0f);
            walkAnimation.Walk(direction, travelled);

            if (pathIndex >= path.Count)
            {
                Rest();
            }
        }


        // 주기적 인간 감지와 타일 경계에서 안전한 추적 경로 전환
        private void UpdateChase(bool detect = true)
        {
            // 인간의 기존 배회 유지 및 좀비 감지 시점 분산
            if (!isZombie)
            {
                return;
            }

            if (detect)
            {
                detectionRemaining -= Time.deltaTime;
            }

            if (detect && detectionRemaining <= 0f)
            {
                detectionRemaining = DetectionInterval;
                chaseTarget = UnitSight.FindVisibleHuman(map, position);
            }

            // 진행 중인 인접 구간을 마친 뒤 경로 교체로 건물 모서리 관통 방지
            if (position != (Vector2)currentTile)
            {
                return;
            }

            if (chaseTarget < 0)
            {
                if (hasChasePath)
                {
                    path.Clear();
                    pathIndex = 0;
                    hasChasePath = false;
                    Rest();
                }

                return;
            }

            Vector2Int destination = map.Humans[chaseTarget];

            if (!hasChasePath || destination != chaseDestination)
            {
                // 최신 인간 위치까지 경로 재탐색과 배회 대기 취소
                UnitPathfinder.FindTargetPath(map, currentTile, destination, path);
                pathIndex = 0;
                chaseDestination = destination;
                hasChasePath = true;

                if (path.Count == 0)
                {
                    walkAnimation.Stop();
                }
            }
        }


        // 도착 및 경로 탐색 실패 후 무작위 대기와 정지 포즈 적용
        private void Rest()
        {
            // 개체 전용 난수에서 다음 목적지 선택까지의 휴식 시간 결정
            restRemaining = Mathf.Lerp(MinimumRest, MaximumRest, (float)random.NextDouble());
            walkAnimation.Stop();
        }
    }
}
