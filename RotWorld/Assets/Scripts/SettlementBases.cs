using System;
using System.Collections.Generic;
using UnityEngine;

namespace RotWorld
{
    /// <summary>
    /// 정착 인원이 기준 이상인 주거 시설 하나의 거점 영역과 표시 색
    /// </summary>
    public sealed class SettlementBase
    {
        // 거점 건물과 지도 범위로 잘라낸 영역 및 생성 시 고정한 색
        public CityBuilding Building { get; }
        public RectInt Area { get; }
        public Color Color { get; }


        // 거점 건물과 영역 및 색 연결
        internal SettlementBase(CityBuilding building, RectInt area, Color color)
        {
            // 거점 판정 대상 건물 누락 차단
            Building = building ?? throw new ArgumentNullException(nameof(building));
            Area = area;
            Color = color;
        }
    }

    /// <summary>
    /// 정착 인원 기준 거점 생성·해제와 거점 영역·색 및 표시 갱신 번호 관리
    /// </summary>
    public sealed class SettlementBases
    {
        // 거점 생성 정착 인원과 해제 정착 인원 및 건물 주변 영역 여백
        public const int MinimumSettlers = 6;
        public const int DisbandSettlers = 1;
        private const int AreaMargin = (int)UnitWander.BuildingViewRadius;

        // 지형과 구분되는 선명한 거점 색의 채도·명도 범위
        private const float MinimumSaturation = .7f;
        private const float SaturationRange = .2f;
        private const float MinimumBrightness = .85f;
        private const float BrightnessRange = .15f;

        // 다른 생성 난수 흐름과 분리한 거점 색 전용 시드 보정값
        private const int ColorSeedSalt = 0x3A5E1;

        // 지도 크기와 거점 색 전용 난수
        private readonly int mapSize;
        private readonly System.Random colorRandom;

        // 생성 순서의 거점 목록과 건물별 거점 조회
        private readonly List<SettlementBase> bases = new List<SettlementBase>();
        private readonly Dictionary<CityBuilding, SettlementBase> byBuilding = new Dictionary<CityBuilding, SettlementBase>();
        public IReadOnlyList<SettlementBase> All => bases;

        // 거점 목록 변경 번호와 거점 인원까지 포함한 표시 변경 번호
        public int AreaVersion { get; private set; }
        public int Version { get; private set; }


        // 지도 크기와 시드로 영역 범위와 색 난수 준비
        internal SettlementBases(int mapSize, int seed)
        {
            // 영역을 잘라낼 지도 크기 확인
            if (mapSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(mapSize));
            }

            this.mapSize = mapSize;
            colorRandom = new System.Random(unchecked(seed ^ ColorSeedSalt));
        }


        // 건물의 거점 정보 조회
        public bool TryGet(CityBuilding building, out SettlementBase settlementBase)
        {
            // 등록되지 않은 건물은 거점 아님
            if (building == null)
            {
                settlementBase = null;
                return false;
            }

            return byBuilding.TryGetValue(building, out settlementBase);
        }


        // 정착 인원이 바뀐 건물의 거점 생성·해제와 표시 번호 갱신
        internal void Refresh(CityBuilding building)
        {
            // 정착 인원 변경 통지 누락 대상 차단
            if (building == null)
            {
                throw new ArgumentNullException(nameof(building));
            }

            // 건물 안 인원이 아닌 이 건물을 정착지로 둔 인원 기준으로 판정
            int settlers = building.SettlerCount;

            // 한 번 생긴 거점은 생성 기준 아래로 줄어도 영역 유지, 정착 인원이 1명 이하로 남으면 해제
            if (byBuilding.TryGetValue(building, out SettlementBase existing))
            {
                if (settlers <= DisbandSettlers)
                {
                    byBuilding.Remove(building);
                    bases.Remove(existing);
                    AreaVersion++;
                }

                Version++;
                return;
            }

            // 생성 기준 이상이 된 주거 시설은 새 영역과 색으로 거점 생성
            if (BuildingCatalog.IsResidential(building.Type) && settlers >= MinimumSettlers)
            {
                var settlementBase = new SettlementBase(building, GetArea(building.Bounds), PickColor());
                byBuilding.Add(building, settlementBase);
                bases.Add(settlementBase);
                AreaVersion++;
                Version++;
            }
        }


        // 건물 영역을 사방 여백만큼 넓히고 지도 범위로 잘라낸 거점 영역 계산
        private RectInt GetArea(RectInt bounds)
        {
            // 지도 가장자리 건물도 지도 밖으로 영역이 나가지 않게 제한
            int xMin = Mathf.Max(0, bounds.xMin - AreaMargin);
            int yMin = Mathf.Max(0, bounds.yMin - AreaMargin);
            int xMax = Mathf.Min(mapSize, bounds.xMax + AreaMargin);
            int yMax = Mathf.Min(mapSize, bounds.yMax + AreaMargin);
            return new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
        }


        // 전용 난수로 채도·명도가 높은 거점 색 선택
        private Color PickColor()
        {
            // 색상은 전체 범위에서 고르고 채도·명도는 하한을 두어 흐린 색 방지
            float hue = (float)colorRandom.NextDouble();
            float saturation = MinimumSaturation + SaturationRange * (float)colorRandom.NextDouble();
            float brightness = MinimumBrightness + BrightnessRange * (float)colorRandom.NextDouble();
            return Color.HSVToRGB(hue, saturation, brightness);
        }
    }
}
