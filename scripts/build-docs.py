#!/usr/bin/env python3
"""Build the documentation site from the Markdown pages in docs/ (see docs/README.md)."""
import argparse
import json
import re
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DOCS = ROOT / 'docs'
CONTENT = ROOT / '.livedocs/content'
OUTPUT = ROOT / 'output'
CONFIG = json.loads((ROOT / '.livedocs/config.json').read_text(encoding='utf-8'))
# FsLiveDocs 0.7.3 loads project assemblies only from absolute paths.
PROJECTS = [str(ROOT / project) for project in CONFIG['projects']]
EXAMPLES_PROJECT = ROOT / '.livedocs/examples/Examples.fsproj'

# The site's sections: their folders under docs/, in sidebar order, with their sidebar labels.
# Everything else in docs/ (plans, specs, notes) stays out of the site.
SECTIONS = {
    'get-started': 'Get started',
    'tutorial': 'Tutorial',
    'concepts': 'Concepts',
    'how-to': 'Task guides',
    'reference': 'Reference',
}
# Where the site is published, for the absolute links that link previews need.
SITE_URL = 'https://onurgumus.github.io/bflat_test/'
# The link-preview image, drawn from docs/social/card.html.
SOCIAL_IMAGE = 'img/social.png'
SOCIAL_IMAGE_ALT = ('C# code from the quiz tutorial next to a blue DOS screen running it: '
                    'the quiz asks 38 + 8 and rejects the answer &quot;seven&quot;.')
# Files and folders copied into the site as they are.
ASSETS = ['index.html', 'content', 'fonts', 'img']


def run(*command):
    print('+', ' '.join(command), flush=True)
    if subprocess.run(command, cwd=ROOT).returncode:
        raise SystemExit(f'Documentation build failed at: {" ".join(command)}')


def prepare():
    """Copy the pages into .livedocs/content, named so FsLiveDocs orders them as the front matter says."""
    if CONTENT.exists():
        shutil.rmtree(CONTENT)
    CONTENT.mkdir(parents=True)
    pages = []
    for number, section in enumerate(SECTIONS, 1):
        for source in sorted((DOCS / section).glob('*.md')):
            body = source.read_text(encoding='utf-8')
            front = re.match(r'---\n(.*?)\n---\n', body, re.S)
            if not front:
                raise SystemExit(f'{source.relative_to(ROOT)}: missing front matter')
            order = re.search(r'^index: (\d+)$', front[1], re.M)
            if not order:
                raise SystemExit(f'{source.relative_to(ROOT)}: front matter has no "index: <n>"')
            # FsLiveDocs orders folders and files by a numeric prefix and drops it from the address.
            name = 'index.md' if source.stem == 'index' else f'{int(order[1]):03d}-{source.stem}.md'
            target = CONTENT / f'{number * 100 + 50:03d}-{section}' / name
            target.parent.mkdir(parents=True, exist_ok=True)
            # Front matter keys FsLiveDocs doesn't know are removed from the front matter only.
            matter = re.sub(r'^(?:category|categoryindex|index): .*\n', '', front[0], flags=re.M)
            target.write_text(matter + body[front.end():], encoding='utf-8')
            pages.append(f'{section}/{source.stem}.html')
    return pages


def social_tags(title, address):
    """The meta tags link previews read, for a guide page."""
    tags = [
        ('property', 'og:type', 'article'),
        ('property', 'og:site_name', CONFIG['siteName']),
        ('property', 'og:title', title),
        ('property', 'og:url', address),
        ('property', 'og:image', SITE_URL + SOCIAL_IMAGE),
        ('property', 'og:image:width', '1200'),
        ('property', 'og:image:height', '630'),
        ('property', 'og:image:alt', SOCIAL_IMAGE_ALT),
        ('name', 'twitter:card', 'summary_large_image'),
    ]
    return ''.join(f'<meta {kind}="{key}" content="{value}">' for kind, key, value in tags)


def finish(pages):
    # The API pages (and llms.txt) document F# projects; this site has only the placeholder.
    # Its reference section takes their place, also for FsLiveDocs's "a" keyboard shortcut.
    shutil.rmtree(OUTPUT / 'api', ignore_errors=True)
    (OUTPUT / 'llms.txt').unlink(missing_ok=True)
    (OUTPUT / 'api').mkdir()
    (OUTPUT / 'api/index.html').write_text(
        '<!doctype html><html lang="en"><head><meta charset="utf-8">'
        '<meta http-equiv="refresh" content="0; url=../reference/index.html"><title>Reference</title></head>'
        '<body><a href="../reference/index.html">Reference</a></body></html>\n')
    for name in ASSETS:
        source = DOCS / name
        if source.is_dir():
            shutil.copytree(source, OUTPUT / name, dirs_exist_ok=True)
        else:
            shutil.copyfile(source, OUTPUT / name)
    for page in OUTPUT.rglob('*.html'):
        text = page.read_text(encoding='utf-8')
        if 'id="sidebar-root"' not in text:
            continue
        depth = len(page.relative_to(OUTPUT).parts) - 1
        up = '../' * depth
        # FsLiveDocs has no setting for extra scripts: add the highlighter for C#, shell and C.
        if 'content/csharp.js' not in text:
            text = text.replace('</body>', f'<script src="{up}content/highlight.min.js" defer></script>'
                                           f'<script src="{up}content/csharp.js" defer></script></body>')
        # Drop what FsLiveDocs adds for the (removed) API pages: links to them (relative ones only, so a
        # link to someone else's api/ survives), the list items they leave empty, and the sidebar's
        # empty "API Reference" section.
        text = re.sub(r'<a\b[^>]*href="(?:\.\./)*api/[^"]*"[^>]*>.*?</a>', '', text, flags=re.S)
        text = re.sub(r'<li data-sidebar-item="true">\s*</li>', '', text)
        text = re.sub(r'<div data-sidebar-section="true">\s*<h3 id="api-reference".*?</ul>\s*</div>', '',
                      text, flags=re.S)
        # Sidebar sections are named after their folders; give them their labels.
        for section, label in SECTIONS.items():
            text = re.sub(rf'(data-docs-group="{section}"><summary[^>]*><span>)[^<]*(</span>)',
                          rf'\g<1>{label}\g<2>', text)
        # Guides use the base prose size on every screen width; FsLiveDocs enlarges it from md up.
        text = re.sub(r'(<main\b[^>]*class="[^"]*?)\s+md:prose-lg', r'\1', text, count=1)
        # Link previews (Open Graph and Twitter): the page's title, its address and the site's card.
        if 'property="og:image"' not in text:
            title = re.search(r'<title>(.*?)</title>', text, re.S)
            title = ' '.join(title[1].split()).replace('"', '&quot;') if title else CONFIG['siteName']
            address = SITE_URL + page.relative_to(OUTPUT).as_posix()
            text = text.replace('</head>', social_tags(title, address) + '</head>', 1)
        # Search indexes page content only, not navigation.
        if 'data-pagefind-body' not in text:
            text, found = re.subn(r'<main(?=[\s>])', '<main data-pagefind-body', text, count=1)
            if not found:
                raise SystemExit(f'{page.relative_to(OUTPUT)}: no <main> element to index for search')
        page.write_text(text, encoding='utf-8')
    missing = [page for page in pages if not (OUTPUT / page).is_file()]
    if missing:
        raise SystemExit('Pages missing from the site: ' + ', '.join(missing))
    print(f'Built {len(pages)} pages.', flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--prepare-only', action='store_true', help='Assemble the pages without building.')
    options = parser.parse_args()
    run(sys.executable, 'scripts/gen-reference.py')
    run(sys.executable, 'scripts/check-doc-snippets.py')
    pages = prepare()
    if options.prepare_only:
        return
    run('dotnet', 'build', '-v', 'q', '-nologo', str(EXAMPLES_PROJECT))
    if OUTPUT.exists():
        shutil.rmtree(OUTPUT)
    run('dotnet', 'livedocs', 'build', *PROJECTS, '--version', 'latest', '--interactive', 'false', '--banner', 'false')
    finish(pages)
    # Index the final site, including the homepage; fail if Pagefind can't.
    run('npx', '--yes', 'pagefind@1.5.2', '--site', 'output')
    run(sys.executable, 'scripts/check-doc-links.py')


if __name__ == '__main__':
    main()
