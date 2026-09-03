import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { of, Subject, throwError } from 'rxjs';
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

  it('preserves safe rate-limit error presentation', () => {
    api.send.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 429, error: { message: 'Too many requests.' } })));
    const { fixture, component } = createAndSend('Hi');
    expect(component.error()).toBeTruthy();
    expect(fixture.nativeElement.querySelector('[role="alert"]')).not.toBeNull();
  });
});
