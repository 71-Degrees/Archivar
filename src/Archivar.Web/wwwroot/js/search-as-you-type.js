/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

// Search as you type, for a server-rendered search form: add data-search-as-you-type to a GET <form> that holds the search box, and the
// search runs by itself once typing pauses - no need to press Search. Everything else on the form (a hidden strand or sort order) is sent
// too; paging cursors are not, so a new search starts at the top. The page is rendered on the server, so the listings change by a reload
// (location.replace - a pause is not a history entry), and the box regains focus with the cursor at the end so typing carries on.
//
// The pause is 350 ms: a little longer than the gap between an ordinary typist's keystrokes (about 150-250 ms at 40-60 words a minute) but
// short enough to feel immediate; each new key pushes it back. Enter or the Search button still search at once. Not mid-IME-composition.
// Optional: data-search-pause="500" on the form changes the pause. Without this script it is the plain Search form.

const DEFAULT_PAUSE_MS = 350;

for (const form of /** @type {NodeListOf<HTMLFormElement>} */ (document.querySelectorAll('form[data-search-as-you-type]'))) {
  const box = /** @type {HTMLInputElement | null} */ (form.querySelector('input[type="search"], input[name="q"]'));
  if (!box) continue;
  const pause = Number(form.getAttribute('data-search-pause')) || DEFAULT_PAUSE_MS;
  const key = `search-focus:${location.pathname}`;
  let timer;
  let last = box.value.trim();

  const search = () => {
    const now = box.value.trim();
    if (now === last) return;
    last = now;
    const url = new URL(form.action, location.href);
    url.search = '';
    for (const [name, value] of new FormData(form)) if (String(value).trim()) url.searchParams.set(name, String(value).trim());
    try { sessionStorage.setItem(key, '1'); } catch { /* private mode: the box just won't regain focus */ }
    location.replace(url);
  };

  box.setAttribute('autocomplete', 'off');
  box.addEventListener('input', (event) => {
    clearTimeout(timer);
    if (!(/** @type {InputEvent} */ (event)).isComposing) timer = setTimeout(search, pause);
  });
  box.addEventListener('compositionend', () => { clearTimeout(timer); timer = setTimeout(search, pause); });
  form.addEventListener('submit', () => clearTimeout(timer));

  try {
    if (sessionStorage.getItem(key)) {
      sessionStorage.removeItem(key);
      box.focus();
      box.setSelectionRange(box.value.length, box.value.length);
    }
  } catch { /* nothing to restore */ }
}
