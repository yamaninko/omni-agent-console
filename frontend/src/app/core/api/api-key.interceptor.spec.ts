import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { installLocalStorageMock } from '../../../test-localstorage';
import { apiKeyInterceptor } from './api-key.interceptor';

describe('apiKeyInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    installLocalStorageMock();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([apiKeyInterceptor])),
        provideHttpClientTesting()
      ]
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('always attaches X-Studio-Session-Id', () => {
    http.get('/api/tasks').subscribe();
    const req = httpMock.expectOne('/api/tasks');
    expect(req.request.headers.get('X-Studio-Session-Id')).toMatch(/^[A-Za-z0-9_-]{8,64}$/);
    req.flush([]);
  });

  it('adds X-Api-Key when console_api_key is present', () => {
    localStorage.setItem('console_api_key', 'lab-secret');
    http.get('/api/tasks').subscribe();
    const req = httpMock.expectOne('/api/tasks');
    expect(req.request.headers.get('X-Api-Key')).toBe('lab-secret');
    req.flush([]);
  });

  it('omits X-Api-Key when no console key is stored', () => {
    http.get('/api/tasks').subscribe();
    const req = httpMock.expectOne('/api/tasks');
    expect(req.request.headers.has('X-Api-Key')).toBe(false);
    req.flush([]);
  });
});
