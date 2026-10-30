import { HttpClient, HttpContext, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ToastService } from '../services/toast/toast.service';
import { AuthenticationService } from './services/authentication.service';
import { SKIP_ERROR_TOAST } from './skip-error-toast.context';
import { tokenInterceptor } from './token.interceptor';

const URL = 'https://test.api/reservations';
const GROUP_SESSIONS_URL = 'https://test.api/guest/groups/grp-1/sessions';
const GUEST_SESSION_URL = 'https://test.api/guest/sessions/session-1/menu';

describe('tokenInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let toastService: jasmine.SpyObj<ToastService>;
  let authService: jasmine.SpyObj<AuthenticationService>;

  beforeEach(() => {
    toastService = jasmine.createSpyObj<ToastService>('ToastService', ['error']);
    authService = jasmine.createSpyObj<AuthenticationService>('AuthenticationService', [
      'getTokens',
    ]);
    authService.getTokens.and.returnValue(null);

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([tokenInterceptor])),
        provideHttpClientTesting(),
        { provide: ToastService, useValue: toastService },
        { provide: AuthenticationService, useValue: authService },
      ],
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('shows the server message in a toast for a 400 response', () => {
    http.post(URL, {}).subscribe({ error: () => undefined });

    httpMock
      .expectOne(URL)
      .flush({ message: 'Overlaps' }, { status: 400, statusText: 'Bad Request' });

    expect(toastService.error).toHaveBeenCalledWith('Overlaps', 'Error');
  });

  it('does not show a toast but still rethrows when SKIP_ERROR_TOAST is set', () => {
    let receivedStatus: number | undefined;
    http
      .post(URL, {}, { context: new HttpContext().set(SKIP_ERROR_TOAST, true) })
      .subscribe({ error: (error) => (receivedStatus = error.status) });

    httpMock
      .expectOne(URL)
      .flush({ message: 'Overlaps' }, { status: 400, statusText: 'Bad Request' });

    expect(toastService.error).not.toHaveBeenCalled();
    expect(receivedStatus).toBe(400);
  });

  it('attaches the Cognito token to Owner endpoints under /guest/groups/', () => {
    authService.getTokens.and.returnValue({ idToken: 'owner-id-token', accessToken: 'access' });
    http.get(GROUP_SESSIONS_URL).subscribe();

    const req = httpMock.expectOne(GROUP_SESSIONS_URL);
    expect(req.request.headers.get('Authorization')).toBe('Bearer owner-id-token');
    req.flush({ participants: [], total: 0 });
  });

  it('keeps the guest session token on /guest/sessions/ endpoints', () => {
    authService.getTokens.and.returnValue({ idToken: 'owner-id-token', accessToken: 'access' });
    http.get(GUEST_SESSION_URL, { headers: { Authorization: 'Bearer guest-token' } }).subscribe();

    const req = httpMock.expectOne(GUEST_SESSION_URL);
    expect(req.request.headers.get('Authorization')).toBe('Bearer guest-token');
    req.flush({ dishes: {} });
  });

  it('does not show a toast for a silent /guest/groups/ request', () => {
    let receivedStatus: number | undefined;
    http
      .get(GROUP_SESSIONS_URL, { context: new HttpContext().set(SKIP_ERROR_TOAST, true) })
      .subscribe({ error: (error) => (receivedStatus = error.status) });

    httpMock
      .expectOne(GROUP_SESSIONS_URL)
      .flush({ error: 'Server error' }, { status: 500, statusText: 'Internal Server Error' });

    expect(toastService.error).not.toHaveBeenCalled();
    expect(receivedStatus).toBe(500);
  });

  it('does not show a toast for a guest endpoint when SKIP_ERROR_TOAST is set', () => {
    http
      .get(GUEST_SESSION_URL, { context: new HttpContext().set(SKIP_ERROR_TOAST, true) })
      .subscribe({ error: () => undefined });

    httpMock
      .expectOne(GUEST_SESSION_URL)
      .flush({ error: 'Not found' }, { status: 404, statusText: 'Not Found' });

    expect(toastService.error).not.toHaveBeenCalled();
  });

  it('still shows a toast for a guest endpoint error by default', () => {
    http.get(GUEST_SESSION_URL).subscribe({ error: () => undefined });

    httpMock
      .expectOne(GUEST_SESSION_URL)
      .flush({ error: 'Session not found' }, { status: 404, statusText: 'Not Found' });

    expect(toastService.error).toHaveBeenCalledWith('Session not found', 'Not Found');
  });
});
