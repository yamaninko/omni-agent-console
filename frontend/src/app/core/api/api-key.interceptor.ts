import { HttpInterceptorFn } from '@angular/common/http';

export const apiKeyInterceptor: HttpInterceptorFn = (req, next) => {
  const apiKey = localStorage.getItem('console_api_key');
  if (apiKey) {
    req = req.clone({
      setHeaders: {
        'X-Api-Key': apiKey
      }
    });
  }
  return next(req);
};
