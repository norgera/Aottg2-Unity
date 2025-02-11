using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace UI
{
    public class ChatScrollRect : ScrollRect 
    {
        private bool isMouseOver = false;
        private bool isDragging = false;
        private Image handleImage;
        private ChatPanel _chatPanel;

        protected override void Start()
        {
            base.Start();
            _chatPanel = GetComponentInParent<ChatPanel>();
            if (verticalScrollbar != null)
            {
                verticalScrollbarVisibility = ScrollbarVisibility.AutoHide;
                handleImage = verticalScrollbar.handleRect.GetComponent<Image>();
                
                // Add listener for scrollbar value changes
                verticalScrollbar.onValueChanged.AddListener(OnScrollbarValueChanged);
                
                // Make sure scrollbar and handle are initially enabled if needed
                verticalScrollbar.gameObject.SetActive(true);
                if (handleImage != null)
                {
                    handleImage.enabled = true;
                }
            }
        }

        private void OnScrollbarValueChanged(float value)
        {
            // This ensures the content updates when using the scrollbar directly
            if (onValueChanged != null)
                onValueChanged.Invoke(new Vector2(0, value));
        }

        protected override void LateUpdate()
        {
            base.LateUpdate();
            
            if (_chatPanel != null && !_chatPanel.IsInputActive() && !isDragging && !isMouseOver)
            {
                verticalNormalizedPosition = 0f;
            }

            if (verticalScrollbar != null)
            {
                
                // Force refresh visibility state
                ScrollbarVisibility currentVisibility = verticalScrollbarVisibility;
                verticalScrollbarVisibility = ScrollbarVisibility.Permanent;
                verticalScrollbarVisibility = ScrollbarVisibility.AutoHide;
                
                if (isDragging)
                {
                    handleImage.enabled = true;
                }
            }
        }

        public void OnMouseEnter()
        {
            isMouseOver = true;
            UpdateHandleVisibility();
        }

        public void OnMouseExit()
        {
            isMouseOver = false;
            // Only disable the handle if we're not dragging
            if (handleImage != null && !isDragging)
            {
                handleImage.enabled = false;
            }
        }

        private void UpdateHandleVisibility()
        {
            if (handleImage != null)
            {
                handleImage.enabled = isMouseOver || isDragging;
            }
        }

        public override void OnBeginDrag(PointerEventData eventData) 
        {
            isDragging = true;
            base.OnBeginDrag(eventData);
        }

        public override void OnEndDrag(PointerEventData eventData) 
        {
            base.OnEndDrag(eventData);
            isDragging = false;
            if (handleImage != null)
            {
                handleImage.enabled = isMouseOver;
            }
        }

        public override void OnDrag(PointerEventData eventData) 
        {
            if (isDragging)
            {
                if (handleImage != null)
                {
                    handleImage.enabled = true;
                }
                base.OnDrag(eventData);
            }
        }

        public override void OnScroll(PointerEventData data)
        {
            // Disable scroll wheel by not calling base
        }
    }
} 