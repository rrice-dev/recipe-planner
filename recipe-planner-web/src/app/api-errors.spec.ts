import { HttpErrorResponse } from '@angular/common/http';
import { apiErrorMessage } from './api-errors';

describe('apiErrorMessage', () => {
  const err = (status: number, error: unknown) => new HttpErrorResponse({ status, error });

  it("uses the API's validation messages", () => {
    expect(apiErrorMessage(err(400, { errors: { Name: ['Too long.'], Unit: ['Required.'] } }), 'x')).toBe('Too long. Required.');
  });

  it("uses a problem's detail or a message", () => {
    expect(apiErrorMessage(err(422, { detail: 'Not a recipe page.' }), 'x')).toBe('Not a recipe page.');
    expect(apiErrorMessage(err(409, { message: 'Already exists.' }), 'x')).toBe('Already exists.');
  });

  it('falls back when there is nothing useful', () => {
    expect(apiErrorMessage(err(500, null), 'Try again.')).toBe('Try again.');
    expect(apiErrorMessage(err(502, '<html>gateway</html>'), 'Try again.')).toBe('Try again.');
    expect(apiErrorMessage(new Error('offline'), 'Try again.')).toBe('Try again.');
  });
});
