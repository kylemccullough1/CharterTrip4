// Pages the testing strip.
//
// A scroll container cannot be moved from C#, and this is the only thing on that panel that needs
// script at all. Global rather than a module so a Blazor @onclick can call it without an import
// dance for one function.
window.mysteryScrollRail = (id, direction) => {
    const rail = document.getElementById(id);
    if (!rail) return;

    // One card plus its gap, so a page lands cleanly on the next card rather than between two.
    const card = rail.querySelector('.sim-card');
    const step = card ? card.getBoundingClientRect().width + 12 : rail.clientWidth;

    rail.scrollBy({ left: step * direction, behavior: 'smooth' });
};
