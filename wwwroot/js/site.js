// Keep menu ellipsis visible when menu is open (desktop only)
(function() {
    if (window.innerWidth < 769) return; // Only run on desktop
    
    const ACTIVE_CLASS = 'menu-active';
    const MAX_DISTANCE = 150;
    const CHECK_INTERVAL = 100;
    const ANIMATION_DELAY = 150;
    
    let checkInterval = null;
    let observer = null;
    
    function findClosestEllipsis(popoverRect, menuEllipsis) {
        let closestEllipsis = null;
        let minDistance = Infinity;
        
        menuEllipsis.forEach(ellipsis => {
            const ellipsisRect = ellipsis.getBoundingClientRect();
            const distance = Math.sqrt(
                Math.pow(popoverRect.right - ellipsisRect.right, 2) + 
                Math.pow(popoverRect.top - ellipsisRect.bottom, 2)
            );
            
            if (distance < minDistance && distance < MAX_DISTANCE) {
                minDistance = distance;
                closestEllipsis = ellipsis;
            }
        });
        
        return closestEllipsis;
    }
    
    function checkMenuState() {
        const popovers = document.querySelectorAll('.mud-popover-paper');
        const menuEllipsis = document.querySelectorAll('.menu-ellipsis');
        
        // Remove menu-active from all first
        document.querySelectorAll(`.user-row.${ACTIVE_CLASS}, .whatsAppGroup-header.${ACTIVE_CLASS}`).forEach(el => {
            el.classList.remove(ACTIVE_CLASS);
        });
        
        // Check each visible popover
        popovers.forEach(popover => {
            const isVisible = popover.offsetParent !== null && 
                             popover.style.display !== 'none' && 
                             popover.style.visibility !== 'hidden';
            
            if (isVisible) {
                const closestEllipsis = findClosestEllipsis(popover.getBoundingClientRect(), menuEllipsis);
                if (closestEllipsis) {
                    const parent = closestEllipsis.closest('.user-row') || closestEllipsis.closest('.whatsAppGroup-header');
                    parent?.classList.add(ACTIVE_CLASS);
                }
            }
        });
    }
    
    function startChecking() {
        if (checkInterval) return;
        checkInterval = setInterval(checkMenuState, CHECK_INTERVAL);
    }
    
    function handleClick(e) {
        const ellipsis = e.target.closest('.menu-ellipsis');
        const parent = ellipsis?.closest('.user-row') || ellipsis?.closest('.whatsAppGroup-header');
        
        if (parent) {
            parent.classList.add(ACTIVE_CLASS);
            setTimeout(checkMenuState, ANIMATION_DELAY);
        } else {
            setTimeout(checkMenuState, ANIMATION_DELAY);
        }
    }
    
    // Watch for popover changes
    function initObserver() {
        if (document.body && !observer) {
            observer = new MutationObserver(checkMenuState);
            observer.observe(document.body, {
                childList: true,
                subtree: true,
                attributes: true,
                attributeFilter: ['style', 'class']
            });
        }
    }
    
    // Wait for Blazor to be ready before initializing
    function initialize() {
        // Wait for DOM and Blazor to be ready
        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', function() {
                waitForBlazor();
            });
        } else {
            waitForBlazor();
        }
    }
    
    function waitForBlazor() {
        // Check if Blazor is available
        if (window.Blazor) {
            // For Blazor Server, wait a bit for the SignalR connection to be established
            // This prevents interop errors when events are triggered before connection is ready
            setTimeout(startInitialization, 200);
        } else {
            // Blazor not loaded yet, wait and retry
            setTimeout(waitForBlazor, 50);
        }
    }
    
    function startInitialization() {
        startChecking();
        document.addEventListener('click', handleClick);
        initObserver();
    }
    
    // Start initialization
    initialize();
})();