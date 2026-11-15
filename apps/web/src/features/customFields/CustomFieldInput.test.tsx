import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import type { ReactNode } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { FieldType, type CustomField, type TicketCustomField } from './api';
import { CustomFieldInput, defaultOf, isSet, valueOf } from './CustomFieldInput';

const field = (type: number, extra: Partial<CustomField> = {}): CustomField =>
  ({ id: `f${type}`, key: `field_${type}`, name: `Field ${type}`, type, options: [], usageCount: 0, ...extra }) as CustomField;

const severity = field(FieldType.select, {
  options: [
    { id: 'low', label: 'Low', color: 'gray', isHidden: false },
    { id: 'old', label: 'Old', color: 'red', isHidden: true },
    { id: 'high', label: 'High', color: 'red', isHidden: false },
  ],
});

describe('custom field values', () => {
  it('reads the value of each kind back as the API takes it', () => {
    const item = (type: number, value: Partial<TicketCustomField>) => ({ field: field(type), options: [], ...value }) as TicketCustomField;
    expect(valueOf(item(FieldType.text, { text: 'ACME' }))).toBe('ACME');
    expect(valueOf(item(FieldType.number, { number: 2.5 }))).toBe(2.5);
    expect(valueOf(item(FieldType.checkbox, { checked: true }))).toBe(true);
    expect(valueOf(item(FieldType.user, { user: { id: 'u1', name: 'A', email: 'a@x' } }))).toBe('u1');
    expect(valueOf(item(FieldType.select, { options: [{ id: 'high', label: 'High', isHidden: false }] }))).toBe('high');
    expect(valueOf(item(FieldType.multiSelect, { options: [{ id: 'a', label: 'A', isHidden: false }, { id: 'b', label: 'B', isHidden: false }] }))).toEqual(['a', 'b']);
    expect(valueOf(item(FieldType.date, {}))).toBeNull();
  });

  it('takes defaults from their JSON and knows an empty value', () => {
    expect(defaultOf(field(FieldType.number, { defaultValue: '3' }))).toBe(3);
    expect(defaultOf(field(FieldType.multiSelect))).toEqual([]);
    expect(isSet('')).toBe(false);
    expect(isSet([])).toBe(false);
    expect(isSet(false)).toBe(true);
  });
});

describe('custom field input', () => {
  afterEach(() => cleanup());

  it('saves typed text once, on blur, and clears with an empty value', () => {
    const onChange = vi.fn();
    render(<CustomFieldInput field={field(FieldType.text)} value="ACME" onChange={onChange} />);
    const input = screen.getByLabelText('Field 0');
    fireEvent.change(input, { target: { value: 'Globex' } });
    expect(onChange).not.toHaveBeenCalled();
    fireEvent.blur(input);
    expect(onChange).toHaveBeenCalledWith('Globex');

    fireEvent.change(input, { target: { value: '  ' } });
    fireEvent.blur(input);
    expect(onChange).toHaveBeenLastCalledWith(null);
  });

  it('keeps a number field to numbers and saves a number', () => {
    const onChange = vi.fn();
    render(<CustomFieldInput field={field(FieldType.number)} value={null} onChange={onChange} />);
    const input = screen.getByLabelText('Field 2');
    fireEvent.change(input, { target: { value: 'abc' } });
    expect((input as HTMLInputElement).value).toBe('');
    fireEvent.change(input, { target: { value: '-1.5' } });
    fireEvent.blur(input);
    expect(onChange).toHaveBeenCalledWith(-1.5);
  });

  it('offers visible options, and a hidden one only to the ticket that has it', () => {
    render(<CustomFieldInput field={severity} value={null} onChange={() => undefined} />);
    fireEvent.mouseDown(screen.getByRole('combobox'));
    expect(screen.queryByRole('option', { name: 'Old' })).toBeNull();
    expect(screen.getByRole('option', { name: 'High' })).toBeTruthy();
    cleanup();

    render(<CustomFieldInput field={severity} value="old" onChange={() => undefined} />);
    fireEvent.mouseDown(screen.getByRole('combobox'));
    expect(screen.getByRole('option', { name: 'Old' })).toBeTruthy();
  });

  it('shows a read-only value as text, options as chips', () => {
    render(<CustomFieldInput field={severity} value="high" disabled onChange={() => undefined} />);
    expect(screen.getByText('High')).toBeTruthy();
    expect(screen.queryByRole('combobox')).toBeNull();
  });

  it('saves a multi-select choice when its list closes', () => {
    const onChange = vi.fn();
    const platforms = field(FieldType.multiSelect, {
      options: [
        { id: 'web', label: 'Web', isHidden: false },
        { id: 'ios', label: 'iOS', isHidden: false },
      ],
    });
    render(<CustomFieldInput field={platforms} value={['web']} onChange={onChange} />);
    fireEvent.click(screen.getByRole('button'));
    fireEvent.click(screen.getByLabelText('iOS'));
    expect(onChange).not.toHaveBeenCalled();
    fireEvent.keyDown(screen.getByRole('group'), { key: 'Escape' });
    expect(onChange).toHaveBeenCalledWith(['web', 'ios']);
  });

  it('marks a required field in a form and offers no "none" once a required select has a value', () => {
    const required = { ...severity, isRequired: true };
    render(<CustomFieldInput field={required} value="high" onChange={() => undefined} />);
    fireEvent.mouseDown(screen.getByRole('combobox', { name: /Field 4 */ }));
    expect(screen.queryByRole('option', { name: 'None' })).toBeNull();
  });

  it('shows tags like labels and saves the typed words', () => {
    const wrap = ({ children }: { children: ReactNode }) => <QueryClientProvider client={new QueryClient()}>{children}</QueryClientProvider>;
    const onChange = vi.fn();
    const tags = field(FieldType.tags);
    render(<CustomFieldInput field={tags} value={['api', 'login']} disabled onChange={onChange} />, { wrapper: wrap });
    expect(screen.getByText('api')).toBeTruthy();
    cleanup();

    render(<CustomFieldInput field={tags} value={['api']} onChange={onChange} />, { wrapper: wrap });
    const input = screen.getByLabelText('Field 9');
    fireEvent.change(input, { target: { value: 'login' } });
    fireEvent.keyDown(input, { key: 'Enter' });
    expect(onChange).toHaveBeenCalledWith(['api', 'login']);
  });
});
