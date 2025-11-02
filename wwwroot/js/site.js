window.adjustContextMenuPosition = (x, y) => {
    const scrollX = window.pageXOffset || document.documentElement.scrollLeft;
    const scrollY = window.pageYOffset || document.documentElement.scrollTop;

    const menuWidth = 180;
    const menuHeight = 120;
    const viewportWidth = window.innerWidth;
    const viewportHeight = window.innerHeight;

    let newX = x + scrollX;
    let newY = y + scrollY;

    if (x + menuWidth > viewportWidth) newX = scrollX + viewportWidth - menuWidth - 10;
    if (y + menuHeight > viewportHeight) newY = scrollY + viewportHeight - menuHeight - 10;

    return { x: newX - 40 , y: newY + 12 };
};
