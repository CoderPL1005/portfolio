import { ChangeDetectionStrategy, Component, input } from '@angular/core';

type InlineToken =
  | { kind: 'text' | 'strong' | 'emphasis' | 'code'; value: string }
  | { kind: 'link'; value: string; href: string; external: boolean };

type MarkdownBlock =
  | { kind: 'paragraph'; content: InlineToken[] }
  | { kind: 'unordered-list' | 'ordered-list'; items: InlineToken[][] }
  | { kind: 'code'; content: string };

@Component({
  selector: 'app-markdown-inline',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @for (token of tokens(); track $index) {
      @switch (token.kind) {
        @case ('strong') { <strong>{{ token.value }}</strong> }
        @case ('emphasis') { <em>{{ token.value }}</em> }
        @case ('code') { <code>{{ token.value }}</code> }
        @case ('link') {
          <a
            [href]="token.href"
            [attr.target]="token.external ? '_blank' : null"
            [attr.rel]="token.external ? 'noopener noreferrer' : null"
          >{{ token.value }}</a>
        }
        @default { {{ token.value }} }
      }
    }
  `,
  styles: `:host{display:contents}`,
})
export class MarkdownInlineComponent {
  readonly tokens = input.required<InlineToken[]>();
}

@Component({
  selector: 'app-assistant-markdown',
  imports: [MarkdownInlineComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @for (block of blocks(); track $index) {
      @switch (block.kind) {
        @case ('paragraph') {
          <p><app-markdown-inline [tokens]="block.content" /></p>
        }
        @case ('unordered-list') {
          <ul>
            @for (item of block.items; track $index) {
              <li><app-markdown-inline [tokens]="item" /></li>
            }
          </ul>
        }
        @case ('ordered-list') {
          <ol>
            @for (item of block.items; track $index) {
              <li><app-markdown-inline [tokens]="item" /></li>
            }
          </ol>
        }
        @case ('code') { <pre><code>{{ block.content }}</code></pre> }
      }
    }
  `,
  styles: `
    :host{display:block;min-width:0;overflow-wrap:anywhere;word-break:break-word;line-height:1.65;color:var(--color-text)}
    p{margin:0 0 .75rem}p:last-child{margin-bottom:0}
    ul,ol{margin:.45rem 0 .8rem;padding-left:1.35rem}li{margin:.3rem 0;padding-left:.15rem}
    strong{color:color-mix(in srgb,var(--color-text) 92%,white);font-weight:750}
    a{color:color-mix(in srgb,var(--color-primary) 78%,white);text-decoration:underline;text-decoration-color:color-mix(in srgb,var(--color-primary) 50%,transparent);text-underline-offset:.18em;overflow-wrap:anywhere}
    a:hover{text-decoration-color:currentColor}
    a:focus-visible{outline:2px solid var(--color-primary);outline-offset:2px;border-radius:.15rem}
    code{border:1px solid var(--color-border);border-radius:.3rem;padding:.08rem .3rem;background:var(--color-surface-lowest);font-family:ui-monospace,SFMono-Regular,Consolas,monospace;font-size:.88em}
    pre{max-width:100%;margin:.65rem 0 .85rem;padding:.75rem;overflow:auto;border:1px solid var(--color-border);border-radius:.55rem;background:var(--color-background);white-space:pre}
    pre code{border:0;padding:0;background:transparent;font-size:.8rem}
  `,
})
export class AssistantMarkdownComponent {
  readonly content = input('');
  readonly blocks = () => parseMarkdown(this.content());
}

function parseMarkdown(markdown: string): MarkdownBlock[] {
  const lines = markdown.replace(/\r\n?/g, '\n').split('\n');
  const blocks: MarkdownBlock[] = [];
  let index = 0;

  while (index < lines.length) {
    if (!lines[index].trim()) {
      index++;
      continue;
    }

    if (/^\s*```/.test(lines[index])) {
      index++;
      const code: string[] = [];
      while (index < lines.length && !/^\s*```\s*$/.test(lines[index])) code.push(lines[index++]);
      if (index < lines.length) index++;
      blocks.push({ kind: 'code', content: code.join('\n') });
      continue;
    }

    const unordered = /^\s*[-*+]\s+(.+)$/;
    if (unordered.test(lines[index])) {
      const items: InlineToken[][] = [];
      while (index < lines.length) {
        const match = lines[index].match(unordered);
        if (!match) break;
        items.push(parseInline(match[1]));
        index++;
      }
      blocks.push({ kind: 'unordered-list', items });
      continue;
    }

    const ordered = /^\s*\d+[.)]\s+(.+)$/;
    if (ordered.test(lines[index])) {
      const items: InlineToken[][] = [];
      while (index < lines.length) {
        const match = lines[index].match(ordered);
        if (!match) break;
        items.push(parseInline(match[1]));
        index++;
      }
      blocks.push({ kind: 'ordered-list', items });
      continue;
    }

    const paragraph: string[] = [];
    while (
      index < lines.length &&
      lines[index].trim() &&
      !/^\s*```/.test(lines[index]) &&
      !unordered.test(lines[index]) &&
      !ordered.test(lines[index])
    ) {
      paragraph.push(lines[index].trim());
      index++;
    }
    blocks.push({ kind: 'paragraph', content: parseInline(paragraph.join(' ')) });
  }

  return blocks;
}

function parseInline(value: string): InlineToken[] {
  const pattern = /(\*\*([^*]+)\*\*|__([^_]+)__|`([^`]+)`|\[([^\]]+)]\(([^)\s]+)\)|\*([^*]+)\*|_([^_]+)_)/g;
  const tokens: InlineToken[] = [];
  let cursor = 0;
  let match: RegExpExecArray | null;

  while ((match = pattern.exec(value))) {
    if (match.index > cursor) tokens.push({ kind: 'text', value: value.slice(cursor, match.index) });
    if (match[2] || match[3]) tokens.push({ kind: 'strong', value: match[2] ?? match[3] });
    else if (match[4]) tokens.push({ kind: 'code', value: match[4] });
    else if (match[5] && match[6]) {
      const href = safeHref(match[6]);
      tokens.push(href
        ? { kind: 'link', value: match[5], href, external: /^https?:\/\//i.test(href) }
        : { kind: 'text', value: match[5] });
    } else tokens.push({ kind: 'emphasis', value: match[7] ?? match[8] });
    cursor = pattern.lastIndex;
  }

  if (cursor < value.length) tokens.push({ kind: 'text', value: value.slice(cursor) });
  return tokens;
}

function safeHref(value: string): string | null {
  return /^(https?:\/\/|mailto:|\/(?!\/)|#)/i.test(value) ? value : null;
}
