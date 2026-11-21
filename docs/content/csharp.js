// Colours the code on every page. FsLiveDocs highlights F# only, so C#, shell and C
// blocks are coloured here with highlight.js (vendored, 11.9). Adapted from FCQRS's
// language-tabs.js, without its F#/C# tabs.
(() => {
  // highlight.js colours a C# type name only where it is declared. Find the other places a type
  // name stands, from the text around it: before a variable or member name, with type arguments,
  // after new/is/as/or/and/not/case, in a pattern, in a base list, or before a static member.
  // `kindAt` gives the highlight.js class at an offset, or '' for unhighlighted code.
  // Returns [start, end) offsets in order.
  function csharpTypeRanges(text, kindAt) {
    const word = /[A-Za-z0-9_]/;
    const plain = i => kindAt(i) === '';
    const generic = [];
    for (let i = 1; i < text.length; i++) {
      if (text[i] !== '<' || !word.test(text[i - 1]) || !plain(i)) continue;
      let depth = 0, j = i;
      for (; j < text.length; j++) {
        if (text[j] === '<') depth++;
        else if (text[j] === '>') { if (--depth === 0) break; }
        else if (!/[\w\s,.?\[\]()]/.test(text[j])) break;
      }
      if (depth === 0 && j < text.length) { generic.push([i, j]); }
    }
    const genericEnd = i => generic.find(range => range[0] === i)?.[1];
    const inGeneric = i => generic.some(range => range[0] < i && i < range[1]);
    const lineOf = i => text.slice(text.lastIndexOf('\n', i - 1) + 1, (text.indexOf('\n', i) + 1 || text.length + 1) - 1);
    // A C# 15 union names its type and then lists its case types.
    const unions = [...text.matchAll(/\bunion\s+\w+\s*(\([^)]*\))?/g)]
      .map(found => [found.index, found.index + found[0].length]);
    const inUnion = i => unions.some(range => range[0] < i && i < range[1]);
    const typeWords = ['new', 'is', 'as', 'or', 'and', 'not', 'case'];
    const ranges = [];
    const name = /(?<![\w@])[A-Z][A-Za-z0-9_]*/g;
    for (let match; (match = name.exec(text));) {
      const start = match.index, end = start + match[0].length;
      if (!plain(start)) continue;
      const line = lineOf(start);
      if (/^\s*(global\s+)?using\s+(static\s+)?[\w.]+\s*;/.test(line)) continue;
      let p = start - 1;
      while (p >= 0 && (/\s/.test(text[p]) || kindAt(p) === 'comment')) p--;
      const before = p >= 0 ? text[p] : '';
      if (before === '.') {
        // new Npgsql.NpgsqlConnection(...): the last name of the path is the type.
        const path = /\bnew\s+[\w.]+\.$/.test(text.slice(Math.max(0, start - 200), start));
        if (path && /[({]/.test(text[end] || '')) ranges.push([start, end]);
        continue;
      }
      let previous = '';
      if (word.test(before)) {
        let q = p;
        while (q >= 0 && word.test(text[q])) q--;
        previous = text.slice(q + 1, p + 1);
        // A name that follows a type is the member or variable being declared.
        const kind = kindAt(p);
        if (kind === '' || kind === 'built_in' || kind === 'title') continue;
      }
      const afterArrow = before === '>' && text[p - 1] === '=';
      if ((before === '>' && !afterArrow) || before === ']') continue;
      if (before === '?' && /[\w>\]]/.test(text[p - 1] || '')) continue;
      let n = end;
      const close = text[n] === '<' ? genericEnd(n) : undefined;
      if (close !== undefined) n = close + 1;
      if (text[n] === '?') n++;
      while (text[n] === '[' && text[n + 1] === ']') n += 2;
      const attached = text[n] || '';
      let m = n;
      while (m < text.length && /\s/.test(text[m])) m++;
      const next = text[m] || '';
      const isNew = previous === 'new';
      let type = false;
      const lineStart = text.slice(text.lastIndexOf('\n', start - 1) + 1, start);
      if (inGeneric(start)) type = true;
      else if (inUnion(start)) type = true;
      else if (isNew && next === '.') type = false;
      else if (next === '(' && /^\(\s*var\b/.test(text.slice(m))) type = true;
      else if (/^(or|and)\b/.test(text.slice(m)) && kindAt(m) === 'keyword') type = true;
      else if (next === '(') type = isNew;
      else if (close !== undefined) type = true;
      else if (attached === ':' && previous !== 'case') type = false;
      else if (typeWords.includes(previous) || /\b(typeof|default|nameof)\($/.test(text.slice(0, start))) type = true;
      else if (m > n && /[A-Za-z_@]/.test(next) && ['', 'title'].includes(kindAt(m))) type = true;
      else if (next === '.' && /[A-Za-z]/.test(text[m + 1] || '')) type = true;
      else if (next === '{') type = true;
      else if (next === '=' && text[m + 1] === '>') type = true;
      else if (before === '(' && (next === ',' || next === ')') && !/[\w>]/.test(text[p - 1] || '')) type = true;
      else if (/\b(class|record|struct|interface)\b.*\s:\s/.test(lineStart)) type = true;
      if (type) ranges.push([start, end]);
    }
    return ranges;
  }
  function colourCsharpTypes(code) {
    // highlight.js gives a declared method the class it gives a declared type. Mark the methods,
    // so that they keep the function colour and differ from the type names around them.
    code.querySelectorAll('.hljs-function > .hljs-title').forEach(title => {
      const gap = title.previousSibling;
      const keyword = gap?.nodeType === Node.TEXT_NODE && !gap.data.trim() ? gap.previousSibling : null;
      const declaresType = keyword?.nodeType === Node.ELEMENT_NODE &&
        ['record', 'class', 'struct', 'interface', 'union', 'enum'].includes(keyword.textContent);
      if (!declaresType) title.classList.add('function_');
    });
    const nodes = [];
    const walker = document.createTreeWalker(code, NodeFilter.SHOW_TEXT);
    let offset = 0;
    for (let node; (node = walker.nextNode());) {
      // highlight.js wraps a declaration and its parameters in spans that leave their code plain.
      const scope = [...node.parentElement.classList].find(name => name.startsWith('hljs-')) || '';
      const kind = ['hljs-function', 'hljs-params', 'hljs-subst'].includes(scope) ? '' : scope.slice(5);
      nodes.push({ node, start: offset, end: offset + node.data.length, kind: node.parentElement === code ? '' : kind });
      offset += node.data.length;
    }
    const at = i => nodes.find(entry => entry.start <= i && i < entry.end);
    const ranges = csharpTypeRanges(code.textContent, i => at(i)?.kind ?? '');
    // Wrap from the end so earlier offsets in the same text node stay valid.
    for (const [start, end] of ranges.reverse()) {
      const entry = at(start);
      if (!entry || end > entry.end) continue;
      const range = document.createRange();
      range.setStart(entry.node, start - entry.start);
      range.setEnd(entry.node, end - entry.start);
      const span = document.createElement('span');
      span.className = 'hljs-type';
      range.surroundContents(span);
    }
  }
  window.addEventListener('DOMContentLoaded', () => {
    if (!window.hljs) return;
    document.querySelectorAll('code[class*="language-"]').forEach(code => {
      const lang = [...code.classList].find(name => name.startsWith('language-')).slice(9);
      if (!['csharp', 'bash', 'sh', 'shell', 'c', 'xml'].includes(lang)) return;
      code.querySelectorAll('br').forEach(br => br.replaceWith('\n'));
      code.textContent = code.textContent;
      window.hljs.highlightElement(code);
      if (lang === 'csharp') colourCsharpTypes(code);
    });
  });
})();
