import { test } from 'node:test';
import assert from 'node:assert/strict';
import { parseScaled, inputScaled, displayScaled } from '../src/scaled.mjs';

test('quantities and prices stay exact at one hundredth', () => {
  assert.equal(parseScaled('125,37'), 12537);
  assert.equal(parseScaled('0.01'), 1);
  assert.equal(parseScaled('1.2'), 120);
  assert.equal(inputScaled(12537), '125.37');
  assert.equal(displayScaled(12537), '125,37');
});
test('invalid or overly precise input is rejected instead of rounded', () => {
  for (const value of ['1.001', '-1', 'NaN', '1e3', '', '1000001']) assert.throws(() => parseScaled(value));
});
