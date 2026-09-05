import { ChangeDetectionStrategy, Component, ElementRef, inject, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { finalize, take } from 'rxjs';
import { ApiHttpError } from '../../core/api/api-error.model';
import { safeAdminError } from '../admin/shared/admin-api';
import { ChatSource } from './agent.models';
import { AgentService } from './agent.service';
import { AssistantMarkdownComponent } from './assistant-markdown.component';

interface Line { role: 'USER' | 'ASSISTANT'; content: string; id?: string; sources?: ChatSource[] }
type FeedbackState = 'PENDING' | 'POSITIVE' | 'NEGATIVE';

const genericRateLimitMessage = 'Bạn đang gửi tin nhắn quá nhanh. Vui lòng đợi một chút rồi thử lại.';
const rateLimitMessages: Readonly<Record<string, string>> = {
  CHAT_RATE_LIMITED: genericRateLimitMessage,
  CHAT_DAILY_LIMIT_REACHED: 'Bạn đã đạt giới hạn sử dụng chatbot trong ngày. Vui lòng thử lại vào ngày mai.',
  CHAT_SESSION_LIMIT_REACHED: 'Phiên trò chuyện này đã đạt giới hạn tin nhắn. Hãy tạo một phiên trò chuyện mới.',
  CHAT_GLOBAL_LIMIT_REACHED: 'Chatbot đã đạt giới hạn sử dụng trong ngày. Vui lòng thử lại sau.',
};

export function chatErrorMessage(error: unknown): string {
  if (error instanceof ApiHttpError && error.status === 429)
    return rateLimitMessages[error.apiError.code] ?? genericRateLimitMessage;
  return safeAdminError(error);
}

@Component({
  selector: 'app-chat-widget',
  imports: [FormsModule, RouterLink, AssistantMarkdownComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button class="launcher" type="button" (click)="toggle()" [attr.aria-expanded]="open()" aria-controls="portfolio-chat">
      {{ open() ? 'Close' : 'Ask AI' }}
    </button>
    @if (open()) {
      <section id="portfolio-chat" class="chat" aria-label="Portfolio assistant">
        <header>
          <strong><span aria-hidden="true">&gt;_</span> Portfolio assistant</strong>
          <button type="button" (click)="open.set(false)" aria-label="Close chat">×</button>
        </header>
        <div #messages class="messages" aria-live="polite">
          @for (line of lines(); track $index) {
            <article class="message" [class.user-message]="line.role === 'USER'" [class.assistant-message]="line.role === 'ASSISTANT'">
              <span class="message-label">{{ line.role === 'USER' ? 'You' : 'Assistant' }}</span>
              @if (line.role === 'ASSISTANT') {
                <app-assistant-markdown class="answer" [content]="line.content" />
              } @else {
                <p class="user-content">{{ line.content }}</p>
              }
              @if (line.sources?.length) {
                <aside class="sources" aria-label="Sources">
                  <strong>Sources</strong>
                  <ol>
                    @for (source of line.sources; track source.rank) {
                      <li>
                        <span class="source-number">{{ source.rank }}</span>
                        @if (source.projectSlug) {
                          <a [routerLink]="['/projects', source.projectSlug]">{{ source.title }}</a>
                        } @else {
                          <span>{{ source.title }}</span>
                        }
                      </li>
                    }
                  </ol>
                </aside>
              }
              @if (line.role === 'ASSISTANT' && line.id) {
                <div class="feedback" aria-label="Rate this answer">
                  <span>Was this helpful?</span>
                  <div class="feedback-actions">
                    <button type="button" (click)="rate(line.id, 'POSITIVE')" [disabled]="feedback()[line.id] !== undefined" [class.selected]="feedback()[line.id] === 'POSITIVE'" [attr.aria-pressed]="feedback()[line.id] === 'POSITIVE'">Helpful</button>
                    <button type="button" (click)="rate(line.id, 'NEGATIVE')" [disabled]="feedback()[line.id] !== undefined" [class.selected]="feedback()[line.id] === 'NEGATIVE'" [attr.aria-pressed]="feedback()[line.id] === 'NEGATIVE'">Not helpful</button>
                  </div>
                  @if (feedback()[line.id] === 'POSITIVE' || feedback()[line.id] === 'NEGATIVE') {
                    <small role="status">Thanks for the feedback.</small>
                  }
                </div>
              }
            </article>
          }
          @if (loading()) {
            <div class="assistant-loading" role="status"><span></span><span></span><span></span><span class="sr-only">Assistant is thinking</span></div>
          }
          @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
        </div>
        <form (ngSubmit)="send()">
          <label class="sr-only" for="chat-message">Message</label>
          <textarea id="chat-message" [(ngModel)]="draft" name="message" maxlength="2000" rows="2" placeholder="Ask about experience, projects, or skills"></textarea>
          <button type="submit" [disabled]="loading() || !draft.trim()">Send</button>
        </form>
      </section>
    }
  `,
  styles: `
    .launcher{position:fixed;right:1.25rem;bottom:1.25rem;z-index:60;border:1px solid color-mix(in srgb,var(--color-primary) 65%,white);border-radius:.75rem;padding:.85rem 1rem;background:var(--color-primary);color:var(--color-on-primary);font-size:.78rem;font-weight:800;box-shadow:0 1rem 2.5rem #0007}
    .chat{position:fixed;right:1.25rem;bottom:5rem;z-index:60;width:min(32rem,calc(100vw - 2.5rem));max-width:42vw;height:min(44rem,calc(100dvh - 7rem));display:grid;grid-template-rows:auto minmax(0,1fr) auto;border:1px solid var(--color-border);border-radius:var(--radius-xl);background:color-mix(in srgb,var(--color-surface) 96%,transparent);box-shadow:0 1.5rem 4rem #0009;backdrop-filter:blur(22px);overflow:hidden}
    .chat header,.chat form{display:flex;align-items:center;gap:.75rem;padding:1rem;background:var(--color-surface-lowest)}
    .chat header{justify-content:space-between;border-bottom:1px solid var(--color-border)}
    .chat header strong{font-size:.88rem;letter-spacing:.02em}.chat header strong span{color:var(--color-primary)}
    .chat header button{display:grid;place-items:center;width:2rem;height:2rem;border:0;border-radius:.45rem;color:var(--color-text-muted);background:transparent;font-size:1.25rem}.chat header button:hover{color:var(--color-text);background:var(--color-surface-high)}
    .messages{min-width:0;padding:1rem;overflow-x:hidden;overflow-y:auto;overscroll-behavior:contain;scrollbar-gutter:stable}
    .message{min-width:0;margin:0 0 1rem;overflow-wrap:anywhere}.message:last-of-type{margin-bottom:.35rem}
    .assistant-message{padding-right:.35rem}.user-message{width:fit-content;max-width:85%;margin-left:auto;padding:.75rem .85rem;border:1px solid color-mix(in srgb,var(--color-primary) 35%,var(--color-border));border-radius:.8rem .8rem .2rem .8rem;background:color-mix(in srgb,var(--color-primary-strong) 18%,var(--color-surface))}
    .message-label{display:block;margin-bottom:.42rem;color:var(--color-text-muted);font-size:.68rem;font-weight:800;letter-spacing:.08em;text-transform:uppercase}.assistant-message .message-label{color:color-mix(in srgb,var(--color-primary) 75%,white)}
    .user-content{margin:0;white-space:pre-wrap;overflow-wrap:anywhere;word-break:break-word;line-height:1.5}
    .sources{margin-top:.9rem;padding-top:.75rem;border-top:1px solid var(--color-border)}.sources>strong{display:block;margin-bottom:.45rem;color:var(--color-text-muted);font-size:.7rem;letter-spacing:.08em;text-transform:uppercase}.sources ol{display:grid;gap:.35rem;margin:0;padding:0;list-style:none}.sources li{display:flex;align-items:flex-start;gap:.45rem;min-width:0;font-size:.78rem;line-height:1.4}.source-number{display:grid;flex:0 0 auto;place-items:center;width:1.25rem;height:1.25rem;border:1px solid var(--color-border);border-radius:.3rem;color:var(--color-primary);font-family:ui-monospace,monospace;font-size:.65rem}.sources a{min-width:0;color:var(--color-text);text-decoration:none;overflow-wrap:anywhere}.sources a:hover{text-decoration:underline;text-underline-offset:.15em}
    .feedback{display:flex;flex-wrap:wrap;align-items:center;gap:.45rem .65rem;margin-top:.85rem;color:var(--color-text-muted);font-size:.72rem}.feedback-actions{display:flex;gap:.4rem}.feedback button{border:1px solid var(--color-border);border-radius:.45rem;padding:.38rem .55rem;color:var(--color-text-muted);background:transparent;font:inherit;font-weight:700}.feedback button:hover:not(:disabled),.feedback button:focus-visible{border-color:var(--color-primary);color:var(--color-text)}.feedback button:focus-visible{outline:2px solid var(--color-primary);outline-offset:2px}.feedback button.selected{border-color:color-mix(in srgb,var(--color-primary) 70%,white);color:var(--color-text);background:color-mix(in srgb,var(--color-primary) 16%,transparent)}.feedback button:disabled{cursor:default;opacity:.7}.feedback small{flex-basis:100%;color:var(--color-text-muted)}
    .assistant-loading{display:flex;align-items:center;gap:.3rem;width:max-content;margin:.4rem 0 1rem;padding:.65rem .8rem;border:1px solid var(--color-border);border-radius:.7rem;background:var(--color-surface-high)}.assistant-loading>span:not(.sr-only){width:.35rem;height:.35rem;border-radius:50%;background:var(--color-primary);animation:pulse 1.1s ease-in-out infinite}.assistant-loading>span:nth-child(2){animation-delay:.12s}.assistant-loading>span:nth-child(3){animation-delay:.24s}@keyframes pulse{0%,70%,100%{opacity:.3;transform:translateY(0)}35%{opacity:1;transform:translateY(-.18rem)}}
    .error{margin:.5rem 0;padding:.65rem .75rem;border:1px solid color-mix(in srgb,var(--color-error) 40%,var(--color-border));border-radius:.55rem;color:var(--color-error);background:color-mix(in srgb,var(--color-error) 8%,transparent);font-size:.8rem;line-height:1.45}
    .chat form{border-top:1px solid var(--color-border)}textarea{min-width:0;flex:1;resize:none;border:1px solid var(--color-border);border-radius:.55rem;padding:.7rem;color:var(--color-text);background:var(--color-background);font:inherit;line-height:1.4}textarea:focus{outline:2px solid color-mix(in srgb,var(--color-primary) 55%,transparent);outline-offset:1px}.chat form>button{flex:0 0 auto;border:0;border-radius:.5rem;padding:.7rem .85rem;color:var(--color-on-primary);background:var(--color-primary);font-weight:800}.chat form>button:disabled{cursor:not-allowed;opacity:.55}
    button{cursor:pointer}@media(max-width:800px){.chat{right:1rem;bottom:4.75rem;width:calc(100vw - 2rem);max-width:none;height:min(42rem,calc(100dvh - 6rem))}}@media(max-width:500px){.launcher{right:.75rem;bottom:.75rem}.chat{right:.5rem;bottom:4.25rem;width:calc(100vw - 1rem);height:calc(100dvh - 5rem);border-radius:.85rem}.chat header,.chat form{padding:.8rem}.messages{padding:.85rem}.user-message{max-width:92%}}
    @media(prefers-reduced-motion:reduce){.assistant-loading>span:not(.sr-only){animation:none}}
  `,
})
export class ChatWidgetComponent {
  private readonly api = inject(AgentService);
  private readonly messagesElement = viewChild<ElementRef<HTMLDivElement>>('messages');
  readonly open = signal(false);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly lines = signal<Line[]>([]);
  readonly feedback = signal<Record<string, FeedbackState>>({});
  session: string | null = null;
  draft = '';

  toggle() {
    this.open.update(value => !value);
    if (this.open() && !this.session) this.start();
  }

  start() {
    this.loading.set(true);
    this.api.createSession().pipe(take(1), finalize(() => this.loading.set(false))).subscribe({
      next: response => {
        this.session = response.sessionId;
        if (response.welcomeMessage) this.lines.set([{ role: 'ASSISTANT', content: response.welcomeMessage }]);
        this.scrollConversation();
      },
      error: error => this.error.set(safeAdminError(error)),
    });
  }

  send() {
    const message = this.draft.trim();
    if (!message || !this.session || this.loading()) return;
    this.lines.update(lines => [...lines, { role: 'USER', content: message }]);
    this.draft = '';
    this.loading.set(true);
    this.error.set(null);
    this.scrollConversation();
    this.api.send(this.session, message).pipe(take(1), finalize(() => this.loading.set(false))).subscribe({
      next: response => {
        this.lines.update(lines => [...lines, { role: 'ASSISTANT', content: response.answer, id: response.messageId, sources: response.sources }]);
        this.scrollConversation();
      },
      error: error => {
        this.error.set(chatErrorMessage(error));
        this.scrollConversation();
      },
    });
  }

  rate(id: string, rating: 'POSITIVE' | 'NEGATIVE') {
    if (this.feedback()[id] !== undefined) return;
    this.feedback.update(state => ({ ...state, [id]: 'PENDING' }));
    this.api.feedback(id, rating).pipe(take(1)).subscribe({
      next: () => this.feedback.update(state => ({ ...state, [id]: rating })),
      error: error => {
        this.feedback.update(state => {
          const next = { ...state };
          delete next[id];
          return next;
        });
        this.error.set(safeAdminError(error));
      },
    });
  }

  private scrollConversation() {
    queueMicrotask(() => {
      const element = this.messagesElement()?.nativeElement;
      if (element) element.scrollTop = element.scrollHeight;
    });
  }
}
