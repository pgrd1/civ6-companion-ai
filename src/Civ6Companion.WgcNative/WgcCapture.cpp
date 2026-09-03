#include <windows.h>
#include <d3d11.h>
#include <dxgi1_2.h>
#include <dwmapi.h>
#include <wincodec.h>
#include <windows.graphics.capture.interop.h>
#include <windows.graphics.directx.direct3d11.interop.h>
#include <winrt/base.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Graphics.Capture.h>
#include <winrt/Windows.Graphics.DirectX.h>
#include <winrt/Windows.Graphics.DirectX.Direct3D11.h>
#include <condition_variable>
#include <mutex>

using namespace winrt;
using namespace winrt::Windows::Foundation;
using namespace winrt::Windows::Graphics::Capture;
using namespace winrt::Windows::Graphics::DirectX;
using namespace winrt::Windows::Graphics::DirectX::Direct3D11;

namespace
{
    constexpr DWORD kFrameWaitTimeoutMilliseconds = 75'000;

    struct unmap_guard
    {
        ID3D11DeviceContext* context; ID3D11Texture2D* texture;
        ~unmap_guard() { context->Unmap(texture, 0); }
    };

    struct handle_guard
    {
        HANDLE handle;
        ~handle_guard() { if (handle) CloseHandle(handle); }
    };

    struct apartment_guard
    {
        ~apartment_guard() { winrt::uninit_apartment(); }
    };

    struct dpi_awareness_guard
    {
        DPI_AWARENESS_CONTEXT previous{};
        HRESULT result{ S_OK };

        dpi_awareness_guard()
        {
            SetLastError(ERROR_SUCCESS);
            previous = SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
            if (!previous)
            {
                const auto error = GetLastError();
                result = HRESULT_FROM_WIN32(error == ERROR_SUCCESS ? ERROR_NOT_SUPPORTED : error);
            }
        }

        ~dpi_awareness_guard()
        {
            if (previous) SetThreadDpiAwarenessContext(previous);
        }
    };

    HRESULT ComputePhysicalWgcCrop(
        RECT const& frame_bounds,
        POINT const client_origin,
        LONG client_width,
        LONG client_height,
        UINT content_width,
        UINT content_height,
        UINT* crop_x,
        UINT* crop_y)
    {
        if (!crop_x || !crop_y || client_width <= 0 || client_height <= 0 || content_width == 0 || content_height == 0)
        {
            return E_INVALIDARG;
        }

        const LONGLONG frame_width = static_cast<LONGLONG>(frame_bounds.right) - frame_bounds.left;
        const LONGLONG frame_height = static_cast<LONGLONG>(frame_bounds.bottom) - frame_bounds.top;
        if (frame_width <= 0 || frame_height <= 0 ||
            frame_width != content_width || frame_height != content_height)
        {
            return E_INVALIDARG;
        }

        const LONGLONG x = static_cast<LONGLONG>(client_origin.x) - frame_bounds.left;
        const LONGLONG y = static_cast<LONGLONG>(client_origin.y) - frame_bounds.top;
        if (x < 0 || y < 0 || x + client_width > content_width || y + client_height > content_height)
        {
            return E_INVALIDARG;
        }

        *crop_x = static_cast<UINT>(x);
        *crop_y = static_cast<UINT>(y);
        return S_OK;
    }

    HRESULT WritePng(
        ID3D11DeviceContext* context,
        ID3D11Texture2D* source,
        UINT crop_x,
        UINT crop_y,
        UINT width,
        UINT height,
        UINT content_width,
        UINT content_height,
        LPCWSTR output)
    {
        if (crop_x > content_width || crop_y > content_height || width == 0 || height == 0 ||
            width > content_width - crop_x || height > content_height - crop_y)
        {
            return E_INVALIDARG;
        }

        D3D11_TEXTURE2D_DESC source_desc{};
        source->GetDesc(&source_desc);
        if (crop_x > source_desc.Width || crop_y > source_desc.Height || width == 0 || height == 0 ||
            width > source_desc.Width - crop_x || height > source_desc.Height - crop_y)
        {
            return E_INVALIDARG;
        }

        D3D11_TEXTURE2D_DESC staging{};
        staging.Width = width; staging.Height = height; staging.MipLevels = 1; staging.ArraySize = 1;
        staging.Format = source_desc.Format; staging.SampleDesc.Count = 1; staging.Usage = D3D11_USAGE_STAGING;
        staging.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
        com_ptr<ID3D11Device> device;
        source->GetDevice(device.put());
        com_ptr<ID3D11Texture2D> texture;
        HRESULT hr = device->CreateTexture2D(&staging, nullptr, texture.put());
        if (FAILED(hr)) return hr;

        D3D11_BOX box{ crop_x, crop_y, 0, crop_x + width, crop_y + height, 1 };
        context->CopySubresourceRegion(texture.get(), 0, 0, 0, 0, source, 0, &box);
        D3D11_MAPPED_SUBRESOURCE mapped{};
        hr = context->Map(texture.get(), 0, D3D11_MAP_READ, 0, &mapped);
        if (FAILED(hr)) return hr;

        unmap_guard unmap{ context, texture.get() };
        com_ptr<IWICImagingFactory> factory;
        hr = CoCreateInstance(CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER, __uuidof(IWICImagingFactory), factory.put_void());
        if (FAILED(hr)) return hr;
        com_ptr<IWICStream> stream;
        hr = factory->CreateStream(stream.put());
        if (FAILED(hr)) return hr;
        hr = stream->InitializeFromFilename(output, GENERIC_WRITE);
        if (FAILED(hr)) return hr;
        com_ptr<IWICBitmapEncoder> encoder;
        hr = factory->CreateEncoder(GUID_ContainerFormatPng, nullptr, encoder.put());
        if (FAILED(hr)) return hr;
        hr = encoder->Initialize(stream.get(), WICBitmapEncoderNoCache);
        if (FAILED(hr)) return hr;
        com_ptr<IWICBitmapFrameEncode> frame;
        com_ptr<IPropertyBag2> properties;
        hr = encoder->CreateNewFrame(frame.put(), properties.put());
        if (FAILED(hr)) return hr;
        hr = frame->Initialize(properties.get());
        if (FAILED(hr)) return hr;
        hr = frame->SetSize(width, height);
        if (FAILED(hr)) return hr;
        WICPixelFormatGUID format = GUID_WICPixelFormat32bppBGRA;
        hr = frame->SetPixelFormat(&format);
        if (FAILED(hr) || format != GUID_WICPixelFormat32bppBGRA) return FAILED(hr) ? hr : E_FAIL;
        hr = frame->WritePixels(height, mapped.RowPitch, mapped.RowPitch * height, static_cast<BYTE*>(mapped.pData));
        if (FAILED(hr)) return hr;
        hr = frame->Commit();
        return FAILED(hr) ? hr : encoder->Commit();
    }
}

extern "C" __declspec(dllexport) HRESULT __stdcall ComputeWgcClientCropForTest(
    int frame_left,
    int frame_top,
    int frame_width,
    int frame_height,
    int client_left,
    int client_top,
    int client_width,
    int client_height,
    int content_width,
    int content_height,
    int* crop_x,
    int* crop_y)
{
    if (!crop_x || !crop_y || frame_width <= 0 || frame_height <= 0 || content_width <= 0 || content_height <= 0)
    {
        return E_INVALIDARG;
    }

    const LONGLONG right = static_cast<LONGLONG>(frame_left) + frame_width;
    const LONGLONG bottom = static_cast<LONGLONG>(frame_top) + frame_height;
    if (right > LONG_MAX || bottom > LONG_MAX)
    {
        return E_INVALIDARG;
    }

    UINT native_crop_x{};
    UINT native_crop_y{};
    const RECT frame_bounds{ frame_left, frame_top, static_cast<LONG>(right), static_cast<LONG>(bottom) };
    const POINT client_origin{ client_left, client_top };
    const auto result = ComputePhysicalWgcCrop(
        frame_bounds,
        client_origin,
        client_width,
        client_height,
        static_cast<UINT>(content_width),
        static_cast<UINT>(content_height),
        &native_crop_x,
        &native_crop_y);
    if (FAILED(result)) return result;

    *crop_x = static_cast<int>(native_crop_x);
    *crop_y = static_cast<int>(native_crop_y);
    return S_OK;
}

extern "C" __declspec(dllexport) HRESULT __stdcall CaptureCivWindowToPng(
    HWND hwnd, int client_x, int client_y, int width, int height, LPCWSTR output_path, HANDLE cancelled)
{
    if (!hwnd || !output_path || width <= 0 || height <= 0 || !cancelled) return E_INVALIDARG;
    try
    {
        winrt::init_apartment(winrt::apartment_type::multi_threaded);
        apartment_guard apartment_cleanup;
        dpi_awareness_guard dpi_awareness;
        if (FAILED(dpi_awareness.result)) return dpi_awareness.result;

        RECT client_bounds{};
        if (!GetClientRect(hwnd, &client_bounds)) return HRESULT_FROM_WIN32(GetLastError());
        POINT physical_client_origin{};
        if (!ClientToScreen(hwnd, &physical_client_origin)) return HRESULT_FROM_WIN32(GetLastError());
        const auto physical_client_width = client_bounds.right - client_bounds.left;
        const auto physical_client_height = client_bounds.bottom - client_bounds.top;
        if (client_x != physical_client_origin.x || client_y != physical_client_origin.y ||
            width != physical_client_width || height != physical_client_height)
        {
            return E_INVALIDARG;
        }

        com_ptr<ID3D11Device> d3d_device;
        com_ptr<ID3D11DeviceContext> context;
        D3D_FEATURE_LEVEL level{};
        HRESULT hr = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            nullptr, 0, D3D11_SDK_VERSION, d3d_device.put(), &level, context.put());
        if (FAILED(hr)) return hr;
        com_ptr<IDXGIDevice> dxgi_device = d3d_device.as<IDXGIDevice>();
        com_ptr<::IInspectable> inspectable;
        hr = CreateDirect3D11DeviceFromDXGIDevice(dxgi_device.get(), inspectable.put());
        if (FAILED(hr)) return hr;
        auto direct3d_device = inspectable.as<IDirect3DDevice>();

        auto interop = get_activation_factory<GraphicsCaptureItem, IGraphicsCaptureItemInterop>();
        GraphicsCaptureItem item{ nullptr };
        check_hresult(interop->CreateForWindow(hwnd, guid_of<ABI::Windows::Graphics::Capture::IGraphicsCaptureItem>(), put_abi(item)));
        auto frame_pool = Direct3D11CaptureFramePool::CreateFreeThreaded(direct3d_device, DirectXPixelFormat::B8G8R8A8UIntNormalized, 1, item.Size());
        auto session = frame_pool.CreateCaptureSession(item);

        std::mutex gate;
        Direct3D11CaptureFrame captured{ nullptr };
        HANDLE frame_ready = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        if (!frame_ready) return HRESULT_FROM_WIN32(GetLastError());
        handle_guard close_event{ frame_ready };
        auto token = frame_pool.FrameArrived(auto_revoke, [&](auto const& sender, auto const&)
        {
            std::scoped_lock lock(gate);
            if (!captured) captured = sender.TryGetNextFrame();
            if (captured) SetEvent(frame_ready);
        });
        session.StartCapture();
        HANDLE handles[] = { frame_ready, cancelled };
        const auto wait = WaitForMultipleObjects(2, handles, FALSE, kFrameWaitTimeoutMilliseconds);
        if (wait == WAIT_OBJECT_0 + 1) return HRESULT_FROM_WIN32(ERROR_CANCELLED);
        if (wait == WAIT_TIMEOUT) return HRESULT_FROM_WIN32(ERROR_TIMEOUT);
        if (wait == WAIT_FAILED) return HRESULT_FROM_WIN32(GetLastError());
        if (wait != WAIT_OBJECT_0) return E_FAIL;

        std::scoped_lock lock(gate);
        if (!captured) return E_FAIL;
        auto access = captured.Surface().as<::Windows::Graphics::DirectX::Direct3D11::IDirect3DDxgiInterfaceAccess>();
        com_ptr<ID3D11Texture2D> source;
        check_hresult(access->GetInterface(__uuidof(ID3D11Texture2D), source.put_void()));
        RECT frame_bounds{};
        hr = DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, &frame_bounds, sizeof(frame_bounds));
        if (FAILED(hr)) return hr;
        const auto content_size = captured.ContentSize();
        if (content_size.Width <= 0 || content_size.Height <= 0) return E_INVALIDARG;
        UINT crop_x{};
        UINT crop_y{};
        hr = ComputePhysicalWgcCrop(
            frame_bounds,
            physical_client_origin,
            physical_client_width,
            physical_client_height,
            static_cast<UINT>(content_size.Width),
            static_cast<UINT>(content_size.Height),
            &crop_x,
            &crop_y);
        if (FAILED(hr)) return hr;
        return WritePng(
            context.get(), source.get(), crop_x, crop_y,
            static_cast<UINT>(width), static_cast<UINT>(height),
            static_cast<UINT>(content_size.Width), static_cast<UINT>(content_size.Height), output_path);
    }
    catch (hresult_error const& error)
    {
        return error.code();
    }
    catch (...)
    {
        return E_FAIL;
    }
}
