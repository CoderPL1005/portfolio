import { TestBed } from '@angular/core/testing';
import { of, Subject, throwError } from 'rxjs';
import { ApiHttpError } from '../../core/api/api-error.model';
import { AgentService } from './agent.service';
import { ChatWidgetComponent } from './chat-widget.component';

describe('ChatWidgetComponent', () => {
  let api: {
    createSession: ReturnType<typeof vi.fn>;
    send: ReturnType<typeof vi.fn>;
    feedback: ReturnType<typeof vi.fn>;
  };

  beforeEach(() => {
    api = {
      createSession: vi.fn(() => of({ sessionId: 's', status: 'ACTIVE', welcomeMessage: 'Welcome' })),
      send: vi.fn(() => of({
        messageId: 'm',
        answer: '**Project overview**\n\n* Angular frontend\n* .NET API\n\nRead the [repository](https://github.com/example/project) and use `dotnet test`.',
        sources: [{ title: 'Project', sourceType: 'PROJECT', sourceRefId: null, projectSlug: 'project', rank: 1, similarityScore: .9 }],
      })),
      feedback: vi.fn(() => of({})),
    };
    TestBed.configureTestingModule({ providers: [{ provide: AgentService, useValue: api }] });
  });

  function createAndSend(message = 'Project?') {
    const fixture = TestBed.createComponent(ChatWidgetComponent);
    const component = fixture.componentInstance;
    component.toggle();
    component.draft = message;
    component.send();
    fixture.detectChanges();
    return { fixture, component };
  }

  it('opens and initializes one session', () => {
    const fixture = TestBed.createComponent(ChatWidgetComponent);
    fixture.componentInstance.toggle();
    fixture.detectChanges();
    expect(api.createSession).toHaveBeenCalledTimes(1);
    expect(fixture.nativeElement.textContent).toContain('Welcome');
  });

  it('renders assistant Markdown as structured, safe content', () => {
    const { fixture } = createAndSend();
    const answer = fixture.nativeElement.querySelector('.assistant-message:last-of-type .answer');
    expect(answer.querySelector('strong')?.textContent).toBe('Project overview');
    expect(answer.textContent).not.toContain('**');
    expect(answer.querySelectorAll('ul li')).toHaveLength(2);
    expect(answer.querySelector('code')?.textContent).toBe('dotnet test');
  });

  it('renders Markdown links as secure clickable anchors', () => {
    const { fixture } = createAndSend();
    const link = fixture.nativeElement.querySelector('.answer a') as HTMLAnchorElement;
    expect(link.getAttribute('href')).toBe('https://github.com/example/project');
    expect(link.getAttribute('target')).toBe('_blank');
    expect(link.getAttribute('rel')).toBe('noopener noreferrer');
  });

  it('keeps user messages as text instead of interpreting HTML or Markdown', () => {
    const { fixture } = createAndSend('<strong>unsafe</strong> **still text**');
    const user = fixture.nativeElement.querySelector('.user-message');
    expect(user.textContent).toContain('<strong>unsafe</strong> **still text**');
    expect(user.querySelector('strong')).toBeNull();
  });

  it('does not turn model HTML or unsafe Markdown URLs into executable elements', () => {
    api.send.mockReturnValue(of({
      messageId: 'm',
      answer: '<img src=x onerror=alert(1)> [unsafe](javascript:evil)',
      sources: [],
    }));
    const { fixture } = createAndSend();
    const answer = fixture.nativeElement.querySelector('.assistant-message:last-of-type .answer');
    expect(answer.querySelector('img')).toBeNull();
    expect(answer.querySelector('a')).toBeNull();
    expect(answer.textContent.replace(/\s+/g, ' ').trim()).toContain('<img src=x onerror=alert(1)> unsafe');
  });

  it('renders structured citations separately from the answer', () => {
    const { fixture } = createAndSend();
    const message = fixture.nativeElement.querySelector('.assistant-message:last-of-type');
    const sources = message.querySelector('.sources');
    expect(sources.textContent).toContain('Sources');
    expect(sources.textContent).toContain('Project');
    expect(sources.querySelector('a')?.getAttribute('href')).toBe('/projects/project');
    expect(message.querySelector('.answer .sources')).toBeNull();
  });

  it('submits feedback once and shows its selected state', () => {
    const { fixture, component } = createAndSend();
    component.rate('m', 'NEGATIVE');
    component.rate('m', 'NEGATIVE');
    fixture.detectChanges();
    expect(api.feedback).toHaveBeenCalledTimes(1);
    expect(api.feedback).toHaveBeenCalledWith('m', 'NEGATIVE');
    const selected = fixture.nativeElement.querySelector('.feedback button.selected');
    expect(selected.textContent).toContain('Not helpful');
    expect(fixture.nativeElement.textContent).toContain('Thanks for the feedback.');
  });

  it('shows loading, blocks duplicate sends, and sanitizes provider failures', () => {
    const pending = new Subject<never>();
    api.send.mockReturnValue(pending);
    const fixture = TestBed.createComponent(ChatWidgetComponent);
    const component = fixture.componentInstance;
    component.toggle();
    component.draft = 'Hi';
    component.send();
    component.draft = 'Again';
    component.send();
    fixture.detectChanges();
    expect(api.send).toHaveBeenCalledTimes(1);
    expect(component.loading()).toBe(true);
    expect(fixture.nativeElement.querySelector('.assistant-loading')).not.toBeNull();
    expect((fixture.nativeElement.querySelector('form button') as HTMLButtonElement).disabled).toBe(true);
    pending.error(new Error('provider credential detail'));
    fixture.detectChanges();
    expect(component.loading()).toBe(false);
    expect(component.error()).toBeTruthy();
    expect(component.error()).not.toContain('credential');
  });

  it.each([
    ['CHAT_RATE_LIMITED', 'Bạn đang gửi tin nhắn quá nhanh. Vui lòng đợi một chút rồi thử lại.'],
    ['CHAT_DAILY_LIMIT_REACHED', 'Bạn đã đạt giới hạn sử dụng chatbot trong ngày. Vui lòng thử lại vào ngày mai.'],
    ['CHAT_SESSION_LIMIT_REACHED', 'Phiên trò chuyện này đã đạt giới hạn tin nhắn. Hãy tạo một phiên trò chuyện mới.'],
    ['CHAT_GLOBAL_LIMIT_REACHED', 'Chatbot đã đạt giới hạn sử dụng trong ngày. Vui lòng thử lại sau.'],
  ])('maps 429 %s to its visitor-facing message', (code, message) => {
    api.send.mockReturnValue(throwError(() => new ApiHttpError(429, { code, message: 'Backend message.' })));

    const { fixture, component } = createAndSend('Hi');

    expect(component.error()).toBe(message);
    expect(fixture.nativeElement.querySelector('[role="alert"]')?.textContent).toContain(message);
  });

  it.each(['UNKNOWN_LIMIT', 'HTTP_429'])('uses the generic message for 429 code %s', code => {
    api.send.mockReturnValue(throwError(() => new ApiHttpError(429, { code, message: 'Unknown limit.' })));

    const { component } = createAndSend('Hi');

    expect(component.error()).toBe('Bạn đang gửi tin nhắn quá nhanh. Vui lòng đợi một chút rồi thử lại.');
  });

  it('ends loading, allows another input, does not retry, and adds no fake assistant answer after 429', () => {
    api.send.mockReturnValue(throwError(() => new ApiHttpError(429, {
      code: 'CHAT_RATE_LIMITED',
      message: 'Too many messages.',
    })));

    const { fixture, component } = createAndSend('Keep this message');
    const textarea = fixture.nativeElement.querySelector('textarea') as HTMLTextAreaElement;

    expect(component.loading()).toBe(false);
    expect(api.send).toHaveBeenCalledTimes(1);
    expect(component.lines().filter(line => line.role === 'USER').map(line => line.content)).toEqual(['Keep this message']);
    expect(component.lines().filter(line => line.role === 'ASSISTANT')).toHaveLength(1);
    expect(textarea.disabled).toBe(false);
    expect((fixture.nativeElement.querySelector('form button') as HTMLButtonElement).disabled).toBe(true);

    api.send.mockReturnValue(of({ messageId: 'next', answer: 'Available again', sources: [] }));
    component.draft = 'Try later';
    component.send();
    expect(api.send).toHaveBeenCalledTimes(2);
    expect(component.lines().at(-1)?.content).toBe('Available again');
  });

  it('preserves existing non-429 API error behavior', () => {
    api.send.mockReturnValue(throwError(() => new ApiHttpError(503, {
      code: 'AGENT_UNAVAILABLE',
      message: 'The portfolio assistant is temporarily unavailable.',
    })));

    const { component } = createAndSend('Hi');

    expect(component.error()).toBe('The portfolio assistant is temporarily unavailable.');
  });
});
