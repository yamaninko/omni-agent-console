import { HttpInterceptorFn } from '@angular/common/http';
import { getStudioSessionId } from './studio-session';

export const apiKeyInterceptor: HttpInterceptorFn = (req, next) => {
  // Session id always travels along; the backend no-ops it in the default
  // profile and scopes requests to it in shared-lab mode.
  const headers: Record<string, string> = {
    'X-Studio-Session-Id': getStudioSessionId()
  };

  const apiKey = localStorage.getItem('console_api_key');
  if (apiKey) {
    headers['X-Api-Key'] = apiKey;
  }

  return next(req.clone({ setHeaders: headers }));
};
