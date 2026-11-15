using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RotWorld
{
    /// <summary>
    /// 거점 영역 위 건물 이름과 정착 인원 라벨 표시와 화면 위치 추적
    /// </summary>
    public sealed class SettlementBaseLabels
    {
        // 지도와 화면 캔버스 및 복제 원본 연결
        private readonly CityMapView mapView;
        private readonly Camera worldCamera;
        private readonly RectTransform canvasRect;
        private readonly RectTransform template;
        private readonly BuildingInfoPanel infoPanel;

        // 텍스트 여백과 최소 줌에서도 읽히는 축소 단계별 표시 크기
        private const float HorizontalPadding = 6f;
        private const float VerticalPadding = 2f;
        private const float AreaGap = 2f;
        private const float MinimumZoomScale = .6f;
        private const float MaximumZoomScale = 1.1f;

        // 흰 글자가 읽히도록 거점 색을 어둡게 만든 라벨 배경
        private const float BackgroundShade = .55f;
        private const float BackgroundAlpha = .9f;

        /// <summary>
        /// 거점 하나에 연결된 라벨과 마지막 표시 인원
        /// </summary>
        private sealed class Label
        {
            public RectTransform Panel;
            public TMP_Text Text;
            public Image Background;
            public SettlementBase Base;
            public int DisplayedCount = -1;
        }

        // 사용 중 라벨과 재사용 대기 라벨
        private readonly List<Label> activeLabels = new List<Label>();
        private readonly Stack<Label> availableLabels = new Stack<Label>();
        private CityMap displayedMap;
        private int displayedVersion = -1;


        // 기존 지도와 UI 원본을 거점 라벨 표시에 연결
        public SettlementBaseLabels(CityMapView mapView, Camera worldCamera, RectTransform canvasRect,
            RectTransform template, BuildingInfoPanel infoPanel)
        {
            // 최초 갱신 전에 필수 참조와 원본 텍스트·배경 확인
            if (mapView == null || worldCamera == null || canvasRect == null || template == null || infoPanel == null ||
                template.GetComponentInChildren<TMP_Text>(true) == null || template.GetComponent<Image>() == null)
            {
                throw new ArgumentException("거점 라벨의 지도, 카메라, 원본 또는 건물 정보 패널 누락");
            }

            this.mapView = mapView;
            this.worldCamera = worldCamera;
            this.canvasRect = canvasRect;
            this.template = template;
            this.infoPanel = infoPanel;
        }


        // 거점 변동 시 라벨 구성 갱신과 매 프레임 화면 위치 추적
        public void Refresh()
        {
            // 지도 교체 시 이전 지도의 라벨 정리
            CityMap map = mapView.Map;

            if (map != displayedMap)
            {
                ReleaseAll();
                displayedMap = map;
                displayedVersion = -1;
            }

            if (map == null || mapView.IsInputBlocked)
            {
                SetLabelsActive(false);
                return;
            }

            // 거점 생성·해제나 거점 인원 변경이 있을 때만 라벨 목록 재구성
            if (displayedVersion != map.Bases.Version)
            {
                displayedVersion = map.Bases.Version;
                RebuildLabels(map.Bases);
            }

            // 변경 통지와 별개로 정착 인원을 직접 비교해 바뀐 라벨만 문자열 갱신하고 화면 위치 추적
            foreach (var label in activeLabels)
            {
                if (label.Base.Building.SettlerCount != label.DisplayedCount)
                {
                    RefreshText(label);
                }

                RefreshPosition(label);
            }
        }


        // 해제된 거점의 라벨 반환과 새 거점의 라벨 배정
        private void RebuildLabels(SettlementBases bases)
        {
            // 해제된 거점의 라벨 반환
            for (int i = activeLabels.Count - 1; i >= 0; i--)
            {
                if (!bases.TryGet(activeLabels[i].Base.Building, out SettlementBase current) || current != activeLabels[i].Base)
                {
                    Release(activeLabels[i]);
                    activeLabels.RemoveAt(i);
                }
            }

            // 새 거점에 라벨 배정 후 표시 인원 미설정 상태로 두어 같은 프레임에 문자열 생성
            foreach (var settlementBase in bases.All)
            {
                if (FindLabel(settlementBase) == null)
                {
                    activeLabels.Add(Acquire(settlementBase));
                }
            }
        }


        // 현재 정착 인원으로 라벨 문자열과 크기 갱신
        private void RefreshText(Label label)
        {
            // 정착 인원이 바뀐 경우에만 호출되어 문자열 생성 최소화
            CityBuilding building = label.Base.Building;
            int count = building.SettlerCount;
            label.DisplayedCount = count;
            string text = building.Name + " [" + count + "]";
            label.Text.text = text;
            Vector2 size = label.Text.GetPreferredValues(text);
            label.Panel.sizeDelta = new Vector2(Mathf.Ceil(size.x) + HorizontalPadding * 2f, Mathf.Ceil(size.y) + VerticalPadding * 2f);
        }


        // 거점 영역 상단 중앙을 화면 좌표로 변환해 라벨 배치
        private void RefreshPosition(Label label)
        {
            // 선택된 건물은 이름 패널과 겹치지 않게 숨김
            if (label.Base.Building == infoPanel.SelectedBuilding)
            {
                label.Panel.gameObject.SetActive(false);
                return;
            }

            RectInt area = label.Base.Area;
            var point = new Vector3(area.center.x, area.yMax, 0f);
            Vector3 screenPosition = worldCamera.WorldToScreenPoint(mapView.transform.TransformPoint(point));

            if (screenPosition.z <= 0f || !worldCamera.pixelRect.Contains(screenPosition))
            {
                label.Panel.gameObject.SetActive(false);
                return;
            }

            // 축소할수록 작아지되 최소 배율을 유지해 전체 보기에서도 표시
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPosition, null, out Vector2 local);
            float scale = Mathf.Lerp(MinimumZoomScale, MaximumZoomScale, mapView.ZoomProgress);
            label.Panel.localScale = Vector3.one * scale;
            label.Panel.anchoredPosition = local + Vector2.up * (AreaGap * scale);
            label.Panel.gameObject.SetActive(true);
        }


        // 지정 거점에 연결된 사용 중 라벨 조회
        private Label FindLabel(SettlementBase settlementBase)
        {
            // 거점 수만큼의 짧은 목록 순회
            foreach (var label in activeLabels)
            {
                if (label.Base == settlementBase)
                {
                    return label;
                }
            }

            return null;
        }


        // 대기 라벨 재사용 또는 원본 복제로 라벨 확보 후 거점 색 배경 지정
        private Label Acquire(SettlementBase settlementBase)
        {
            // 원본과 같은 캔버스 위치에 복제하고 텍스트·배경 참조는 생성 시 한 번만 조회
            Label label;

            if (availableLabels.Count > 0)
            {
                label = availableLabels.Pop();
            }
            else
            {
                // 복제본을 원본 순서에 끼워 넣어 원본보다 나중에 만든 건물·인간 정보 패널 아래에 표시
                var panel = UnityEngine.Object.Instantiate(template, template.parent, false);
                panel.SetSiblingIndex(template.GetSiblingIndex());
                panel.name = "Settlement Base Label";
                label = new Label
                {
                    Panel = panel,
                    Text = panel.GetComponentInChildren<TMP_Text>(true),
                    Background = panel.GetComponent<Image>()
                };
            }

            // 영역 색을 어둡게 한 배경으로 라벨과 영역 연결
            Color color = settlementBase.Color;
            label.Background.color = new Color(color.r * BackgroundShade, color.g * BackgroundShade, color.b * BackgroundShade,
                BackgroundAlpha);
            label.Base = settlementBase;
            label.DisplayedCount = -1;
            return label;
        }


        // 라벨을 숨기고 대기 목록에 반환
        private void Release(Label label)
        {
            // 거점 참조를 해제해 지도 교체 후 이전 건물 유지 방지
            label.Base = null;
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
