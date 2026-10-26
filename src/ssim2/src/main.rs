//! ssim2: SSIMULACRA2 scores from fast-ssim2, reading PNG and JPEG the way libjxl's ssimulacra2 tool does.
//!
//!   ssim2 <reference> <distorted>   prints the score, like ssimulacra2
//!   ssim2 <reference> -             prepares the reference once, then reads one candidate path per line from stdin
//!                                   and prints one line per candidate: the score, or "error: " and the reason
//!
//! Matching libjxl: alpha is blended in the image's own encoding, on 0.1 and 0.9 backgrounds with the lower score
//! kept when the reference has alpha (0.5 for a candidate with alpha against an opaque reference); images with an ICC
//! profile that isn't sRGB go through lcms2 to linear sRGB (relative colorimetric, black point compensation); JPEGs
//! are decoded by libjpeg-turbo with its default settings (accurate DCT, smooth upsampling).

use std::borrow::Cow;
use std::ffi::CStr;
use std::io::{BufRead, Write};
use std::process::ExitCode;

use fast_ssim2::{PixelDescriptor, PixelSlice, Ssimulacra2Reference};
use lcms2::{CIExyY, CIExyYTRIPLE, Flags, Intent, PixelFormat, Profile, ToneCurve, Transform};

type Error = Box<dyn std::error::Error>;

/// A decoded image: interleaved samples, gray or RGB, with or without alpha.
struct Image {
    width: u32,
    height: u32,
    gray: bool,
    alpha: bool,
    /// 1 or 2 bytes per sample; 16-bit samples are stored in native byte order.
    sample_size: usize,
    samples: Vec<u8>,
    /// The ICC profile, unless the image is sRGB (no profile, or one that matches sRGB).
    icc: Option<Vec<u8>>,
}

impl Image {
    fn channels(&self) -> usize {
        (if self.gray { 1 } else { 3 }) + usize::from(self.alpha)
    }

    fn sample(&self, i: usize) -> f32 {
        if self.sample_size == 1 {
            f32::from(self.samples[i]) / 255.0
        } else {
            f32::from(u16::from_ne_bytes([self.samples[2 * i], self.samples[2 * i + 1]])) / 65535.0
        }
    }

    /// The pixels as fast-ssim2 reads them, with alpha blended on `background` (an encoded value, 0 to 1).
    fn prepare(&self, background: f32) -> Result<(Cow<'_, [u8]>, PixelDescriptor, usize), Error> {
        let width = self.width as usize;

        // Opaque sRGB is passed as it is: 8-bit images then take fast-ssim2's path that matches libjxl exactly.
        if !self.alpha && self.icc.is_none() {
            let descriptor = match (self.gray, self.sample_size) {
                (true, 1) => PixelDescriptor::GRAY8_SRGB,
                (false, 1) => PixelDescriptor::RGB8_SRGB,
                (true, _) => PixelDescriptor::GRAY16_SRGB,
                (false, _) => PixelDescriptor::RGB16_SRGB,
            };
            return Ok((Cow::Borrowed(&self.samples), descriptor, width * self.channels() * self.sample_size));
        }

        // Otherwise libjxl's order: blend in the image's own encoding, then convert to linear sRGB.
        let channels = self.channels();
        let color = if self.gray { 1 } else { 3 };
        let pixels = width * self.height as usize;
        let mut encoded = vec![0f32; pixels * color];
        for pixel in 0..pixels {
            let alpha = if self.alpha { self.sample(pixel * channels + color) } else { 1.0 };
            for c in 0..color {
                encoded[pixel * color + c] = alpha * self.sample(pixel * channels + c) + (1.0 - alpha) * background;
            }
        }

        let linear = match &self.icc {
            Some(icc) => to_linear(icc, self.gray, &encoded)?,
            None => encoded.iter().map(|&v| srgb_to_linear(v)).collect(),
        };
        let descriptor = if self.gray { PixelDescriptor::GRAYF32_LINEAR } else { PixelDescriptor::RGBF32_LINEAR };
        let bytes = linear.iter().flat_map(|v| v.to_ne_bytes()).collect();
        Ok((Cow::Owned(bytes), descriptor, width * color * 4))
    }

    fn score_against(&self, reference: &Ssimulacra2Reference, background: f32) -> Result<f64, Error> {
        let (bytes, descriptor, stride) = self.prepare(background)?;
        Ok(reference.compare(&PixelSlice::new(&bytes, self.width, self.height, stride, descriptor)?)?)
    }
}

fn srgb_to_linear(v: f32) -> f32 {
    if v <= 0.04045 { v / 12.92 } else { ((v + 0.055) / 1.055).powf(2.4) }
}

fn linear_srgb_profile(gray: bool) -> Result<Profile, Error> {
    let d65 = CIExyY { x: 0.3127, y: 0.3290, Y: 1.0 };
    let linear = ToneCurve::new(1.0);
    Ok(if gray {
        Profile::new_gray(&d65, &linear)?
    } else {
        let primaries = CIExyYTRIPLE {
            Red: CIExyY { x: 0.64, y: 0.33, Y: 1.0 },
            Green: CIExyY { x: 0.30, y: 0.60, Y: 1.0 },
            Blue: CIExyY { x: 0.15, y: 0.06, Y: 1.0 },
        };
        Profile::new_rgb(&d65, &primaries, &[&linear, &linear, &linear])?
    })
}

/// Converts encoded samples in the ICC profile's space to linear sRGB, with the settings libjxl gives lcms2.
fn to_linear(icc: &[u8], gray: bool, encoded: &[f32]) -> Result<Vec<f32>, Error> {
    let source = Profile::new_icc(icc)?;
    let target = linear_srgb_profile(gray)?;
    let flags = Flags::NO_CACHE | Flags::BLACKPOINT_COMPENSATION | Flags::HIGHRES_PRECALC;
    let mut out = vec![0f32; encoded.len()];
    if gray {
        let t: Transform<f32, f32, _, _> = Transform::new_flags(&source, PixelFormat::GRAY_FLT, &target,
            PixelFormat::GRAY_FLT, Intent::RelativeColorimetric, flags)?;
        t.transform_pixels(encoded, &mut out);
    } else {
        let t: Transform<[f32; 3], [f32; 3], _, _> = Transform::new_flags(&source, PixelFormat::RGB_FLT, &target,
            PixelFormat::RGB_FLT, Intent::RelativeColorimetric, flags)?;
        t.transform_pixels(as_rgb(encoded), as_rgb_mut(&mut out));
    }
    Ok(out)
}

fn as_rgb(v: &[f32]) -> &[[f32; 3]] {
    // SAFETY: [f32; 3] has the layout of three f32s, and the length is a multiple of 3.
    unsafe { std::slice::from_raw_parts(v.as_ptr().cast(), v.len() / 3) }
}

fn as_rgb_mut(v: &mut [f32]) -> &mut [[f32; 3]] {
    // SAFETY: as above.
    unsafe { std::slice::from_raw_parts_mut(v.as_mut_ptr().cast(), v.len() / 3) }
}

/// Whether an RGB profile maps colors as sRGB does. libjxl recognizes such profiles and uses the exact sRGB curve,
/// so these images are passed as plain sRGB too.
fn is_srgb(icc: &[u8]) -> bool {
    let steps = [0.0, 0.05, 0.2, 0.5, 0.8, 1.0];
    let mut samples = Vec::with_capacity(steps.len().pow(3) * 3);
    for r in steps {
        for g in steps {
            for b in steps {
                samples.extend([r, g, b]);
            }
        }
    }
    to_linear(icc, false, &samples)
        .is_ok_and(|linear| samples.iter().zip(linear).all(|(&e, l)| (srgb_to_linear(e) - l).abs() < 2e-3))
}

fn decode(path: &str) -> Result<Image, Error> {
    let data = std::fs::read(path).map_err(|e| format!("{path}: {e}"))?;
    let mut image = if data.starts_with(b"\x89PNG") {
        decode_png(&data)
    } else if data.starts_with(&[0xFF, 0xD8]) {
        decode_jpeg(&data)
    } else {
        Err("not a PNG or JPEG".into())
    }
    .map_err(|e| format!("{path}: {e}"))?;

    if image.width < 8 || image.height < 8 {
        return Err(format!("{path}: minimum image size is 8x8 pixels").into());
    }
    if !image.gray && image.icc.as_deref().is_some_and(is_srgb) {
        image.icc = None;
    }
    Ok(image)
}

fn decode_png(data: &[u8]) -> Result<Image, Error> {
    let mut decoder = png::Decoder::new(std::io::Cursor::new(data));
    decoder.set_transformations(png::Transformations::EXPAND);
    let mut reader = decoder.read_info()?;
    // An sRGB chunk wins over an embedded profile, as in libjxl.
    let icc = match (&reader.info().srgb, &reader.info().icc_profile) {
        (None, Some(profile)) => Some(profile.to_vec()),
        _ => None,
    };
    let mut buffer = vec![0; reader.output_buffer_size().ok_or("image too large")?];
    let frame = reader.next_frame(&mut buffer)?;
    buffer.truncate(frame.buffer_size());
    let (gray, alpha) = match frame.color_type {
        png::ColorType::Grayscale => (true, false),
        png::ColorType::GrayscaleAlpha => (true, true),
        png::ColorType::Rgb => (false, false),
        png::ColorType::Rgba => (false, true),
        png::ColorType::Indexed => return Err("palette not expanded".into()),
    };
    let sample_size = if frame.bit_depth == png::BitDepth::Sixteen {
        for pair in buffer.as_chunks_mut::<2>().0 {
            *pair = u16::from_be_bytes(*pair).to_ne_bytes();
        }
        2
    } else {
        1
    };
    Ok(Image { width: frame.width, height: frame.height, gray, alpha, sample_size, samples: buffer, icc })
}

fn decode_jpeg(data: &[u8]) -> Result<Image, Error> {
    use turbojpeg_sys::*;

    struct Handle(tjhandle);
    impl Drop for Handle {
        fn drop(&mut self) {
            // SAFETY: the handle came from tj3Init and is destroyed once.
            unsafe { tj3Destroy(self.0) }
        }
    }

    // SAFETY: every call gets the live handle and buffers of the sizes TurboJPEG reports.
    unsafe {
        let handle = Handle(tj3Init(TJINIT_TJINIT_DECOMPRESS as i32));
        let h = handle.0;
        if h.is_null() {
            return Err("libjpeg-turbo failed to start".into());
        }
        let error = || -> Error { CStr::from_ptr(tj3GetErrorStr(h)).to_string_lossy().into_owned().into() };

        // Keep the APP2 markers, which hold the ICC profile.
        tj3Set(h, TJPARAM_TJPARAM_SAVEMARKERS as i32, 2);
        if tj3DecompressHeader(h, data.as_ptr(), data.len() as _) != 0 {
            return Err(error());
        }
        let width = tj3Get(h, TJPARAM_TJPARAM_JPEGWIDTH as i32);
        let height = tj3Get(h, TJPARAM_TJPARAM_JPEGHEIGHT as i32);
        let colorspace = tj3Get(h, TJPARAM_TJPARAM_COLORSPACE as i32);
        if colorspace == TJCS_TJCS_CMYK as i32 || colorspace == TJCS_TJCS_YCCK as i32 {
            return Err("CMYK JPEGs aren't supported".into());
        }
        if tj3Get(h, TJPARAM_TJPARAM_PRECISION as i32) != 8 {
            return Err("only 8-bit JPEGs are supported".into());
        }

        let mut icc_ptr: *mut u8 = std::ptr::null_mut();
        let mut icc_size = 0;
        let icc = if tj3GetICCProfile(h, &mut icc_ptr, &mut icc_size) == 0 && !icc_ptr.is_null() {
            let profile = std::slice::from_raw_parts(icc_ptr, icc_size as usize).to_vec();
            tj3Free(icc_ptr.cast());
            Some(profile)
        } else {
            None
        };

        let gray = colorspace == TJCS_TJCS_GRAY as i32;
        let mut samples = vec![0u8; width as usize * height as usize * if gray { 1 } else { 3 }];
        let format = if gray { TJPF_TJPF_GRAY } else { TJPF_TJPF_RGB } as i32;
        if tj3Decompress8(h, data.as_ptr(), data.len() as _, samples.as_mut_ptr(), 0, format) != 0 {
            return Err(error());
        }
        Ok(Image { width: width as u32, height: height as u32, gray, alpha: false, sample_size: 1, samples, icc })
    }
}

/// The reference, prepared once per background: 0.1 and 0.9 when it has alpha (the lower score wins), else 0.5,
/// which only matters for a candidate with alpha.
struct Scorer {
    width: u32,
    height: u32,
    prepared: Vec<(f32, Ssimulacra2Reference)>,
}

impl Scorer {
    fn new(reference: Image) -> Result<Self, Error> {
        let backgrounds: &[f32] = if reference.alpha { &[0.1, 0.9] } else { &[0.5] };
        let mut prepared = Vec::with_capacity(backgrounds.len());
        for &background in backgrounds {
            let (bytes, descriptor, stride) = reference.prepare(background)?;
            let pixels = PixelSlice::new(&bytes, reference.width, reference.height, stride, descriptor)?;
            prepared.push((background, Ssimulacra2Reference::new(&pixels)?));
        }
        // The decoded reference is dropped here; only the prepared pyramids stay.
        Ok(Self { width: reference.width, height: reference.height, prepared })
    }

    fn score(&self, candidate: &Image) -> Result<f64, Error> {
        if candidate.width != self.width || candidate.height != self.height {
            return Err("image size mismatch".into());
        }
        let mut lowest = f64::INFINITY;
        for (background, reference) in &self.prepared {
            lowest = lowest.min(candidate.score_against(reference, *background)?);
        }
        Ok(lowest)
    }
}

fn run() -> Result<(), Error> {
    let args: Vec<String> = std::env::args().collect();
    if args.len() != 3 {
        return Err("usage: ssim2 <reference> <distorted>, or ssim2 <reference> - with candidate paths on stdin".into());
    }
    let scorer = Scorer::new(decode(&args[1])?)?;

    if args[2] != "-" {
        println!("{:.8}", scorer.score(&decode(&args[2])?)?);
        return Ok(());
    }

    let mut out = std::io::stdout().lock();
    for line in std::io::stdin().lock().lines() {
        let path = line?;
        if path.is_empty() {
            continue;
        }
        match decode(&path).and_then(|candidate| scorer.score(&candidate)) {
            Ok(score) => writeln!(out, "{score:.8}")?,
            Err(e) => writeln!(out, "error: {e}")?,
        }
        out.flush()?;
    }
    Ok(())
}

fn main() -> ExitCode {
    match run() {
        Ok(()) => ExitCode::SUCCESS,
        Err(e) => {
            eprintln!("{e}");
            ExitCode::FAILURE
        }
    }
}
