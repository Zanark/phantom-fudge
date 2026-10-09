(() => {
  const search = document.getElementById('search');
  const status = document.getElementById('matches');
  const files = [...document.querySelectorAll('.source-file')];
  const links = new Map([...document.querySelectorAll('.file-index a')].map(link => [link.dataset.file, link]));
  const searchable = new Map(files.map(file => [file, file.textContent.toLowerCase()]));
  const filter = () => {
    const query = search.value.trim().toLowerCase();
    let visible = 0;
    for (const file of files) {
      const matches = searchable.get(file).includes(query);
      file.hidden = !matches;
      links.get(file.id).hidden = !matches;
      if (matches) visible++;
    }
    status.textContent = `${visible} of ${files.length} files shown`;
  };
  search.addEventListener('input', filter);
  const revealHash = () => {
    let fragment;
    try { fragment = decodeURIComponent(location.hash.slice(1)); }
    catch (error) { if (error instanceof URIError) return; throw error; }
    const target = document.getElementById(fragment);
    if (!target) return;
    if (target.closest('.source-file')?.hidden) { search.value = ''; filter(); }
    const source = target.closest('.full-source');
    if (source) source.open = true;
    target.scrollIntoView({ behavior:'instant', block:'start' });
  };
  addEventListener('hashchange', revealHash);
  let previousQuery = '';
  addEventListener('beforeprint', () => { previousQuery = search.value; search.value = ''; filter(); });
  addEventListener('afterprint', () => { search.value = previousQuery; filter(); });
  revealHash();
})();
