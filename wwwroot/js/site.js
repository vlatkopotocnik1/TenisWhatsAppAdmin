window.adjustContextMenuPosition = (x, y) => {
    const scrollX = window.scrollX || document.documentElement.scrollLeft;
    const scrollY = window.scrollY || document.documentElement.scrollTop;

    const menuWidth = 180;
    const menuHeight = 120;
    const viewportWidth = window.innerWidth;
    const viewportHeight = window.innerHeight;

    let newX = x + scrollX;
    let newY = y + scrollY;

    if (x + menuWidth > viewportWidth)
        newX = scrollX + viewportWidth - menuWidth - 10;

    if (y + menuHeight > viewportHeight)
        newY = scrollY + viewportHeight - menuHeight - 10;

    return { x: newX - 40, y: newY + 12 };
};

// Keep menu ellipsis visible when menu is open (desktop only)
(function() {
    // Only run on desktop
    if (window.innerWidth < 769) return;
    
    let checkInterval = null;
    
    function checkMenuState() {
        const popovers = document.querySelectorAll('.mud-popover-paper');
        const menuEllipsis = document.querySelectorAll('.menu-ellipsis');
        
        // First, remove menu-active from all
        document.querySelectorAll('.user-row.menu-active, .whatsAppGroup-header.menu-active').forEach(el => {
            el.classList.remove('menu-active');
        });
        
        // Check each popover to see if it's visible
        popovers.forEach(popover => {
            const isVisible = popover.offsetParent !== null && 
                             popover.style.display !== 'none' && 
                             popover.style.visibility !== 'hidden';
            
            if (isVisible) {
                // Find which ellipsis this popover belongs to by checking position
                const popoverRect = popover.getBoundingClientRect();
                let closestEllipsis = null;
                let minDistance = Infinity;
                
                menuEllipsis.forEach(ellipsis => {
                    const ellipsisRect = ellipsis.getBoundingClientRect();
                    // Calculate distance between popover and ellipsis
                    const distance = Math.sqrt(
                        Math.pow(popoverRect.right - ellipsisRect.right, 2) + 
                        Math.pow(popoverRect.top - ellipsisRect.bottom, 2)
                    );
                    
                    if (distance < minDistance && distance < 150) {
                        minDistance = distance;
                        closestEllipsis = ellipsis;
                    }
                });
                
                if (closestEllipsis) {
                    const parent = closestEllipsis.closest('.user-row') || closestEllipsis.closest('.whatsAppGroup-header');
                    if (parent) {
                        parent.classList.add('menu-active');
                    }
                }
            }
        });
    }
    
    // Check menu state periodically and on DOM changes
    function startChecking() {
        if (checkInterval) return;
        checkInterval = setInterval(checkMenuState, 100);
    }
    
    function stopChecking() {
        if (checkInterval) {
            clearInterval(checkInterval);
            checkInterval = null;
        }
    }
    
    // Start checking when page loads
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', startChecking);
    } else {
        startChecking();
    }
    
    // Also check on clicks
    document.addEventListener('click', function(e) {
        const ellipsis = e.target.closest('.menu-ellipsis');
        if (ellipsis) {
            const parent = ellipsis.closest('.user-row') || ellipsis.closest('.whatsAppGroup-header');
            if (parent) {
                // Immediately show ellipsis when clicked
                parent.classList.add('menu-active');
                // Check state after menu animation
                setTimeout(checkMenuState, 150);
            }
        } else {
            // Click outside - check if menus are still open
            setTimeout(checkMenuState, 150);
        }
    });
    
    // Watch for popover changes
    const observer = new MutationObserver(function() {
        checkMenuState();
    });
    
    observer.observe(document.body, {
        childList: true,
        subtree: true,
        attributes: true,
        attributeFilter: ['style', 'class']
    });
})();