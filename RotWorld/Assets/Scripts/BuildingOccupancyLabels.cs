using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace RotWorld
{
    /// <summary>
    /// 인간이 있는 건물 위 인원 수 라벨 표시와 화면 위치 추적
    /// </summary>
    public sealed class BuildingOccupancyLabels
    {
        // 지도와 화면 캔버스 및 복제 원본 연결
        private readonly CityMapView mapView;
        private readonly Camera worldCamera;
        private readonly RectTransform canvasRect;
        private readonly RectTransform template;
        private readonly BuildingInfoPanel infoPanel;

        // 텍스트 여백과 지도 축소 단계별 표시 크기
        private const float HorizontalPadding = 5f;
        private const float VerticalPadding = 2f;
        private const float BuildingGap = 4f;
        private const float MinimumZoomScale = .25f;
        private const float MaximumZoomScale = 1.1f;

        /// <summary>
        /// 건물 하나에 연결된 라벨과 마지막 표시 인원
        /// </summary>
        private sealed class Label
        {
            public RectTransform Panel;
            public TMP_Text Text;
            public CityBuilding Building;
            public int DisplayedCount = -1;
        }

        // 사용 중 라벨과 재사용 대기 라벨 및 건물별 외형 상단 캐시
        private readonly List<Label> activeLabels = new List<Label>();
        private readonly Stack<Label> availableLabels = new Stack<Label>();
        private readonly Dictionary<CityBuilding, float> visualTops = new Dictionary<CityBuilding, float>();
        private CityMap displayedMap;
        private int displayedVersion = -1;


        // 기존 지도와 UI 원본을 인원 라벨 표시에 연결
        public BuildingOccupancyLabels(CityMapView mapView, Camera worldCamera, RectTransform canvasRect,
            RectTransform template, BuildingInfoPanel infoPanel)
        {
            // 최초 갱신 전에 필수 참조와 원본 텍스트 확인
            if (mapView == null || worldCamera == null || canvasRect == null || template == null || infoPanel == null ||
                template.GetComponentInChildren<TMP_Text>(true) == null)
            {
                throw new ArgumentException("건물 인원 라벨의 지도, 카메라, 원본 또는 건물 정보 패널 누락");
            }

            this.mapView = mapView;
            this.worldCamera = worldCamera;
            this.canvasRect = canvasRect;
            this.template = template;
            this.infoPanel = infoPanel;
        }


        // 인원 변동 시 라벨 구성 갱신과 매 프레임 화면 위치 추적
        public void Refresh()
        {
            // 지도 교체 시 이전 지도의 라벨과 외형 캐시 정리
            CityMap map = mapView.Map;

            if (map != displayedMap)
            {
                ReleaseAll();
                visualTops.Clear();
                displayedMap = map;
                displayedVersion = -1;
            }

            if (map == null || mapView.IsInputBlocked)
            {
                SetLabelsActive(false);
                return;
            }

            // 인원 기록 변경 번호가 달라진 경우에만 라벨 목록 재구성
            if (displayedVersion != map.Occupancy.Version)
            {
                displayedVersion = map.Occupancy.Version;
                RebuildLabels(map.Occupancy);
            }

            foreach (var label in activeLabels)
            {
                RefreshPosition(label);
            }
        }


        // 인원이 있는 건물마다 라벨을 배정하고 표시 인원 갱신
        private void RebuildLabels(BuildingOccupancy occupancy)
        {
            // 비어진 건물의 라벨 반환 후 새 건물에 라벨 배정
            for (int i = activeLabels.Count - 1; i >= 0; i--)
            {
                if (occupancy.GetCount(activeLabels[i].Building) == 0)
                {
                    Release(activeLabels[i]);
                    activeLabels.RemoveAt(i);
                }
            }

            foreach (var building in occupancy.OccupiedBuildings)
            {
                if (FindLabel(building) == null)
                {
                    activeLabels.Add(Acquire(building));
                }
            }

            // 인원이 바뀐 라벨만 텍스트와 크기 갱신
            foreach (var label in activeLabels)
            {
                int count = occupancy.GetCount(label.Building);

                if (count == label.DisplayedCount)
                {
                    continue;
                }

                label.DisplayedCount = count;
                string text = "[" + count + "]";
                label.Text.text = text;
                Vector2 size = label.Text.GetPreferredValues(text);
                label.Panel.sizeDelta = new Vector2(Mathf.Ceil(size.x) + HorizontalPadding * 2f, Mathf.Ceil(size.y) + VerticalPadding * 2f);
            }
        }


        // 건물 상단 중앙을 화면 좌표로 변환해 라벨 배치
        private void RefreshPosition(Label label)
        {
            // 선택된 건물은 이름 패널에 인원이 표시되므로 겹침 방지를 위해 숨김
            if (label.Building == infoPanel.SelectedBuilding)
            {
                label.Panel.gameObject.SetActive(false);
                return;
            }

            // 외형 조합 비용을 줄이기 위해 건물별 상단 높이 캐시
            if (!visualTops.TryGetValue(label.Building, out float top))
            {
                top = mapView.GetBuildingVisualTop(label.Building);
                visualTops.Add(label.Building, top);
            }

            var point = new Vector3(label.Building.Bounds.center.x, top, 0f);
            Vector3 screenPosition = worldCamera.WorldToScreenPoint(mapView.transform.TransformPoint(point));

            if (screenPosition.z <= 0f || !worldCamera.pixelRect.Contains(screenPosition))
            {
                label.Panel.gameObject.SetActive(false);
                return;
            }

            // 지도 줌 단계에 따라 라벨과 건물 위 여백을 함께 축소
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPosition, null, out Vector2 local);
            float scale = Mathf.Lerp(MinimumZoomScale, MaximumZoomScale, mapView.ZoomProgress);
            label.Panel.localScale = Vector3.one * scale;
            label.Panel.anchoredPosition = local + Vector2.up * (BuildingGap * scale);
            label.Panel.gameObject.SetActive(true);
        }


        // 지정 건물에 연결된 사용 중 라벨 조회
        private Label FindLabel(CityBuilding building)
        {
            // 동시에 인원이 있는 건물 수만큼의 짧은 목록 순회
            foreach (var label in activeLabels)
            {
                if (label.Building == building)
                {
                    return label;
                }
            }

            return null;
        }


        // 대기 라벨 재사용 또는 원본 복제로 라벨 확보
        private Label Acquire(CityBuilding building)
        {
            // 원본과 같은 캔버스 위치에 복제하고 텍스트 참조는 생성 시 한 번만 조회
            Label label;

            if (availableLabels.Count > 0)
            {
                label = availableLabels.Pop();
            }
            else
            {
                var panel = UnityEngine.Object.Instantiate(template, template.parent, false);
                panel.name = "Building Occupancy";
                label = new Label { Panel = panel, Text = panel.GetComponentInChildren<TMP_Text>(true) };
            }

            label.Building = building;
            label.DisplayedCount = -1;
            return label;
        }


        // 라벨을 숨기고 대기 목록에 반환
        private void Release(Label label)
        {
            // 건물 참조를 해제해 지도 교체 후 이전 건물 유지 방지
            label.Building = null;
            label.Panel.gameObject.SetActive(false);
            availableLabels.Push(label);
        }


        // 사용 중 라벨 전체 반환
        private void ReleaseAll()
        {
            // 역순 반환으로 목록 정리
            for (int i = activeLabels.Count - 1; i >= 0; i--)
            {
                Release(activeLabels[i]);
            }

            activeLabels.Clear();
        }


        // 사용 중 라벨 표시 일괄 전환
        private void SetLabelsActive(bool isActive)
        {
            // 생성창으로 지도 입력이 막힌 동안 라벨 숨김
            foreach (var label in activeLabels)
            {
                label.Panel.gameObject.SetActive(isActive);
            }
        }
    }
}
