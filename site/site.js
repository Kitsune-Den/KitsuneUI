// Copy buttons on code blocks, the guide's "you are here" menu, and the landing page's
// with / without switch. Everything works without this script; it only adds comfort.

document.querySelectorAll('.copy').forEach((button) => {
  button.addEventListener('click', async () => {
    const pre = button.closest('.code').querySelector('pre');
    try {
      await navigator.clipboard.writeText(pre.innerText);
      button.textContent = 'Copied';
    } catch {
      button.textContent = 'Select and copy';
    }
    setTimeout(() => { button.textContent = 'Copy'; }, 1600);
  });
});

const tocLinks = [...document.querySelectorAll('.toc a[href^="#"]')];
if (tocLinks.length) {
  const sections = tocLinks.map((a) => document.querySelector(a.getAttribute('href'))).filter(Boolean);
  const mark = () => {
    let current = sections[0];
    for (const s of sections) {
      if (s.getBoundingClientRect().top < 140) current = s;
    }
    if (window.innerHeight + window.scrollY >= document.body.scrollHeight - 4) current = sections[sections.length - 1];
    tocLinks.forEach((a) => {
      const on = a.getAttribute('href') === '#' + current.id;
      a.classList.toggle('on', on);
      if (on) {
        a.setAttribute('aria-current', 'location');
        // on narrow screens the menu is a strip: keep the current chip in view
        if (a.parentElement.scrollWidth > a.parentElement.clientWidth) a.parentElement.scrollLeft = a.offsetLeft - 16;
      } else a.removeAttribute('aria-current');
    });
  };
  window.addEventListener('scroll', mark, { passive: true });
  mark();
}

const demo = document.querySelector('.demo');
document.querySelectorAll('.seg button').forEach((button) => {
  button.addEventListener('click', () => {
    const plain = button.dataset.mode === 'plain';
    demo.classList.toggle('plain', plain);
    demo.setAttribute('aria-label', plain
      ? 'The same trader window without KitsuneUI: flat grey, as the mod ships it'
      : 'A trader window in KitsuneUI glass: gold title, rounded rows, green Buy and red Close buttons');
    document.querySelectorAll('.seg button').forEach((b) => b.setAttribute('aria-pressed', String(b === button)));
  });
});
