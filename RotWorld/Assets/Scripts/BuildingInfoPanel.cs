using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace RotWorld
{
    /// <summary>
    /// 클릭한 건물 이름과 내부 인원 수 및 수색한 건물의 재고 표시와 지붕 위 화면 위치 추적
    /// </summary>
    public sealed class BuildingInfoPanel
    {
        // 지도와 화면 캔버스 및 표시 요소 연결
        private readonly CityMapView mapView;
        private readonly Camera worldCamera;
        private readonly RectTransform canvasRect;
        private readonly RectTransform panel;
        private readonly TMP_Text nameLabel;

        // 주거 시설 정착 인원·보관량 또는 빈 재고 안내 줄과 ItemCatalog.All 순서의 읽기 전용 재고 칸
        private readonly TMP_Text storageLabel;
        private readonly RectTransform[] stockSlots;
        private readonly TMP_Text[] stockLabels;
        private const float StorageFontSize = 14f;
        private const float StockSlotFontSize = 11f;
        private const string EmptyStockText = "재고 없음";
        private const string SearchingText = "수색 중";
        private const string SearchingSeparator = " · ";

        // 텍스트 밀착 여백과 지도 축소 단계별 표시 크기
        private const float FontSize = 16f;
        private const float HorizontalPadding = 6f;
        private const float VerticalPadding = 3f;
        private const float BuildingGap = 4f;
        private const float MinimumZoomScale = .25f;
        private const float MaximumZoomScale = 1.1f;

        // 선택 건물과 지도 교체 감지 및 선택 시 계산한 외형 상단
        private CityBuilding selectedBuilding;
        private CityMap selectedMap;
        private float selectedTop;
        public CityBuilding SelectedBuilding => selectedBuilding;

        // 마지막으로 표시한 내부 인원 수와 정착 인원 및 재고 버전과 수색 여부
        private int displayedCount = -1;
        private int displayedSettlerCount = -1;
        private int displayedStockVersion = -1;
        private bool isDisplayedSearched;

        // 기존 UI 클릭의 지도 선택 전파 차단
        private readonly PointerEventData pointer;
        private readonly List<RaycastResult> raycasts = new List<RaycastResult>();


        // 기존 지도와 UI 요소를 건물 선택 표시에 연결
        public BuildingInfoPanel(CityMapView mapView, Camera worldCamera, RectTransform canvasRect,
            RectTransform panel, TMP_Text nameLabel, TMP_Text storageLabel, RectTransform[] stockSlots, TMP_Text[] stockLabels)
        {
            // 최초 갱신 전에 필수 참조와 입력 시스템 확인
            if (mapView == null || worldCamera == null || canvasRect == null || panel == null ||
                nameLabel == null || storageLabel == null || EventSystem.current == null)
            {
                throw new ArgumentException("건물 정보 UI의 지도, 카메라, 표시 요소 또는 이벤트 시스템 누락");
            }

            this.stockSlots = stockSlots ?? throw new ArgumentNullException(nameof(stockSlots));
            this.stockLabels = stockLabels ?? throw new ArgumentNullException(nameof(stockLabels));

            if (stockSlots.Length != ItemCatalog.All.Count || stockLabels.Length != stockSlots.Length)
            {
                throw new ArgumentException("건물 재고 칸 수가 아이템 종류 수와 다름");
            }

            for (int i = 0; i < stockSlots.Length; i++)
            {
                if (stockSlots[i] == null || stockLabels[i] == null)
                {
                    throw new ArgumentException("건물 재고 칸 표시 요소 누락");
                }
            }

            this.mapView = mapView;
            this.worldCamera = worldCamera;
            this.canvasRect = canvasRect;
            this.panel = panel;
            this.nameLabel = nameLabel;
            this.storageLabel = storageLabel;
            pointer = new PointerEventData(EventSystem.current);

            // 고정 폭과 줄바꿈을 제거하고 이름 줄을 패널 상단에 고정
            ConfigureLabel(nameLabel, FontSize);
            ConfigureLabel(storageLabel, StorageFontSize);
            PlaceTopRow(nameLabel.rectTransform, VerticalPadding, 0f);

            // 재고 칸은 인간 소지품 칸과 같은 크기로 패널 왼쪽 위 기준 배치
            for (int i = 0; i < stockSlots.Length; i++)
            {
                stockSlots[i].anchorMin = new Vector2(0f, 1f);
                stockSlots[i].anchorMax = new Vector2(0f, 1f);
                stockSlots[i].pivot = new Vector2(0f, 1f);
                stockSlots[i].sizeDelta = Vector2.one * HumanNeedsPanel.SlotSize;
                stockLabels[i].fontSize = StockSlotFontSize;
                stockLabels[i].enableAutoSizing = false;
                stockLabels[i].alignment = TextAlignmentOptions.Center;
                stockSlots[i].gameObject.SetActive(false);
            }

            storageLabel.gameObject.SetActive(false);
        }


        // 일시정지와 무관한 건물 선택 및 카메라 변경 추적
        public void Refresh(bool canSelect)
        {
            // 소환 모드와 생성창 진입 및 지도 교체 후 오래된 선택 제거
            if (!canSelect || mapView.IsInputBlocked || selectedMap != mapView.Map)
            {
                selectedBuilding = null;
                selectedMap = mapView.Map;
                panel.gameObject.SetActive(false);
            }

            if (!canSelect || mapView.IsInputBlocked)
            {
                return;
            }

            // UI 외부 왼쪽 클릭만 처리하며 인물 선택을 우선
            var mouse = Mouse.current;

            if (mouse != null && Application.isFocused && mouse.leftButton.wasPressedThisFrame)
            {
                pointer.position = mouse.position.ReadValue();
                raycasts.Clear();
                EventSystem.current.RaycastAll(pointer, raycasts);

                if (raycasts.Count == 0)
                {
                    var clicked = mapView.GetHumanAtScreenPosition(pointer.position) == null
                        ? mapView.GetBuildingAtScreenPosition(pointer.position) : null;
                    selectedBuilding = clicked == selectedBuilding ? null : clicked;

                    if (selectedBuilding != null)
                    {
                        // 외형 조합 비용을 매 프레임 반복하지 않도록 선택 시 한 번만 상단 계산
                        selectedTop = mapView.GetBuildingVisualTop(selectedBuilding);
                        displayedCount = -1;
                        displayedSettlerCount = -1;
                        displayedStockVersion = -1;
                        isDisplayedSearched = false;
                    }
                }
            }

            if (selectedBuilding == null)
            {
                panel.gameObject.SetActive(false);
                return;
            }

            // 선택 변경 또는 인원·정착 인원·재고 변동과 첫 수색 시에만 텍스트와 패널 크기 갱신
            int count = mapView.Map.Occupancy.GetCount(selectedBuilding);
            int settlerCount = selectedBuilding.SettlerCount;
            int stockVersion = selectedBuilding.StockVersion;
            bool isSearched = selectedBuilding.IsSearched;

            if (count != displayedCount || settlerCount != displayedSettlerCount || stockVersion != displayedStockVersion ||
                isSearched != isDisplayedSearched)
            {
                displayedCount = count;
                displayedSettlerCount = settlerCount;
                displayedStockVersion = stockVersion;
                isDisplayedSearched = isSearched;
                RefreshContent();
            }

            RefreshPosition();
        }


        // 선택한 건물 이름과 인원 수 및 수색한 건물 재고의 실제 크기에 맞춰 패널 갱신
        private void RefreshContent()
        {
            // 선택이나 인원·재고 변경 시에만 이름 줄의 폰트 기준 권장 크기 계산하고 인원이 없으면 이름만 표시
            string text = displayedCount > 0 ? selectedBuilding.Name + " [" + displayedCount + "]" : selectedBuilding.Name;
            nameLabel.text = text;
            Vector2 nameSize = nameLabel.GetPreferredValues(text);
            float width = Mathf.Ceil(nameSize.x) + HorizontalPadding * 2f;
            float height = Mathf.Ceil(nameSize.y) + VerticalPadding * 2f;
            nameLabel.rectTransform.sizeDelta = new Vector2(-HorizontalPadding * 2f, Mathf.Ceil(nameSize.y));

            // 인간이 한 번도 들어가 보지 않은 건물은 이름 줄만 표시
            var stock = selectedBuilding.Stock;
            bool showsStock = selectedBuilding.IsSearched;
            int slotCount = 0;

            // 주거 시설은 찾아낸 보관량과 용량, 그 외 건물은 수색 중이거나 빈 재고일 때만 안내 줄 표시
            bool isResidential = BuildingCatalog.IsResidential(selectedBuilding.Type);
            bool isSearching = showsStock && !selectedBuilding.IsSearchComplete;
            string storage = !showsStock ? null : isResidential
                ? (isSearching ? SearchingText + SearchingSeparator : "") + "보관 " + selectedBuilding.RevealedSupplyCount +
                    " / " + selectedBuilding.StorageCapacity
                : isSearching ? SearchingText : selectedBuilding.TotalStockCount == 0 ? EmptyStockText : null;

            // 주거 시설은 수색 여부와 무관하게 보관량 줄 위에 정착 인원과 정원 줄 추가
            if (isResidential)
            {
                string settlers = "정착 " + displayedSettlerCount + " / " + selectedBuilding.SettlerCapacity;
                storage = storage == null ? settlers : settlers + "\n" + storage;
            }

            storageLabel.gameObject.SetActive(storage != null);

            if (storage != null)
            {
                // 이름 아래에 보관량 또는 빈 재고 안내 줄 배치
                storageLabel.text = storage;
                Vector2 storageSize = storageLabel.GetPreferredValues(storage);
                PlaceTopRow(storageLabel.rectTransform, height, Mathf.Ceil(storageSize.y));
                width = Mathf.Max(width, Mathf.Ceil(storageSize.x) + HorizontalPadding * 2f);
                height += Mathf.Ceil(storageSize.y) + VerticalPadding;
            }

            if (showsStock)
            {
                // 수색으로 찾아낸 아이템 종류만 카탈로그 순서로 칸 내용 갱신해 하나씩 늘어나게 표시
                for (int i = 0; i < stockSlots.Length && i < stock.Count; i++)
                {
                    int count = selectedBuilding.GetRevealedCount(i);

                    if (count > 0)
                    {
                        stockLabels[slotCount].text = ItemCatalog.All[i].Name + "\n" + count;
                        slotCount++;
                    }
                }
            }

            // 사용한 칸만 활성화하고 남은 칸은 숨김
            for (int i = 0; i < stockSlots.Length; i++)
            {
                stockSlots[i].gameObject.SetActive(i < slotCount);
            }

            // 인간 소지품 칸과 같은 열 수로 재고 칸을 가운데 정렬
            if (slotCount > 0)
            {
                int columns = Mathf.Min(slotCount, HumanNeedsPanel.SlotColumns);
                int rows = (slotCount + HumanNeedsPanel.SlotColumns - 1) / HumanNeedsPanel.SlotColumns;
                float step = HumanNeedsPanel.SlotSize + HumanNeedsPanel.SlotGap;
                float gridWidth = columns * step - HumanNeedsPanel.SlotGap;
                width = Mathf.Max(width, gridWidth + HorizontalPadding * 2f);
                float left = (width - gridWidth) * .5f;

                for (int i = 0; i < slotCount; i++)
                {
                    stockSlots[i].anchoredPosition = new Vector2(left + i % HumanNeedsPanel.SlotColumns * step,
                        -height - i / HumanNeedsPanel.SlotColumns * step);
                }

                height += rows * step - HumanNeedsPanel.SlotGap + VerticalPadding;
            }

            panel.sizeDelta = new Vector2(width, height);
        }


        // 고정 크기 글자와 줄바꿈 없는 가운데 정렬 적용
        private static void ConfigureLabel(TMP_Text label, float fontSize)
        {
            // 패널 폭 계산에 권장 크기를 쓰도록 자동 크기 조절 비활성화
            label.fontSize = fontSize;
            label.enableAutoSizing = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.alignment = TextAlignmentOptions.Center;
        }


        // 패널 상단에서 지정 거리만큼 내려온 좌우 여백 포함 한 줄 배치
        private static void PlaceTopRow(RectTransform row, float top, float height)
        {
            // 패널 폭을 따라 늘어나는 상단 기준 줄 영역
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = Vector2.one;
            row.pivot = new Vector2(.5f, 1f);
            row.anchoredPosition = new Vector2(0f, -top);
            row.sizeDelta = new Vector2(-HorizontalPadding * 2f, height);
        }


        // 건물 상단 중앙을 화면 좌표로 변환해 이름 패널 배치
        private void RefreshPosition()
        {
            // 선택 시 저장한 외형 상단에 맞춰 지도 부모 변환과 카메라 줌 반영
            var bounds = selectedBuilding.Bounds;
            var top = new Vector3(bounds.center.x, selectedTop, 0f);
            Vector3 screenPosition = worldCamera.WorldToScreenPoint(mapView.transform.TransformPoint(top));

            if (screenPosition.z <= 0f || !worldCamera.pixelRect.Contains(screenPosition))
            {
                panel.gameObject.SetActive(false);
                return;
            }

            // 지도 줌 단계에 따라 패널과 자식 텍스트 및 건물 위 여백을 함께 축소
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPosition, null, out Vector2 local);
            float scale = Mathf.Lerp(MinimumZoomScale, MaximumZoomScale, mapView.ZoomProgress);
            panel.localScale = Vector3.one * scale;
            panel.anchoredPosition = local + Vector2.up * (BuildingGap * scale);
            panel.gameObject.SetActive(true);
        }
    }
}
