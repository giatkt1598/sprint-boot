import { createContext, use, useCallback, useMemo, useState, type ReactNode } from 'react';
import { Toast, type ToastKind } from './Toast';

interface ToastMessage {
  id: number;
  kind: ToastKind;
  message: string;
}

const ToastContext = createContext<(kind: ToastKind, message: string) => void>(() => undefined);

export const useToast = () => use(ToastContext);

const VISIBLE_MS = 5000;

export function ToastHost({ children }: { children: ReactNode }) {
  const [items, setItems] = useState<ToastMessage[]>([]);

  const show = useCallback((kind: ToastKind, message: string) => {
    const id = Date.now() + Math.random();
    setItems((current) => [...current, { id, kind, message }]);
    window.setTimeout(() => setItems((current) => current.filter((item) => item.id !== id)), VISIBLE_MS);
  }, []);

  const value = useMemo(() => show, [show]);

  return (
    <ToastContext value={value}>
      {children}
      <div className="fixed bottom-6 left-6 z-50 flex flex-col gap-2">
        {items.map((item) => (
          <Toast key={item.id} kind={item.kind} message={item.message} />
        ))}
      </div>
    </ToastContext>
  );
}
